using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// What the file system does with a PDF's input file (audit finding H-9: the same document at
/// the same fuel took 6.4 to 99.5 s). The 90 s of a slow run were the deletion of the input file,
/// and they need no sandbox: on a machine that inspects new files, a document whose one stream
/// inflates to a gigabyte, written, closed and deleted seven seconds later, holds the delete for
/// 90 s; deleted at once, or left alone, it holds nothing. The ways of handing a PDF to the guest
/// and taking it away again are tried here over a document that is new each time (a different
/// tail, so nothing answers from a cache), with a clock on each step. A diagnostic: it prints, it
/// asserts nothing, and it runs only when asked for (<c>--filter Category=Diagnostics</c>); on a
/// machine that inspects nothing every step is a millisecond.
/// </summary>
[TestFixture]
[Explicit("Diagnostics: prints what handing a PDF to the sandbox as a file costs; minutes.")]
[Category("Diagnostics")]
[NonParallelizable]
public sealed class PdfInputFileDiagnosticsTests
{
    [TestCase("write, close, wait as long as a run without one, delete", 3)]
    [TestCase("write, close, run, delete", 3)]
    [TestCase("hold open, run, delete on close", 3)]
    [TestCase("write, close, run, overwrite next", 4)]
    [TestCase("write, close, run, leave, delete folder later", 3)]
    public void WhatTakingAPdfAwayCostsTest(string way, int runs)
    {
        var modulePath = PdfTestModule.Require();
        var bomb = PdfHostileSet.Build().First(entry => entry.Name == "bomb-content-1g.pdf").Bytes;
        var root = Path.Combine(Path.GetTempPath(), $"witai_corpus_h9f_{Guid.NewGuid():N}");
        var inDirectory = Path.Combine(root, "pdf-in");
        var outDirectory = Path.Combine(root, "pdf-out");
        Directory.CreateDirectory(inDirectory);
        Directory.CreateDirectory(outDirectory);

        using var host = new WasmHost();
        using var module = WasmModule.FromBytes(host.Engine, "pdftext", File.ReadAllBytes(modulePath));
        var random = new Random(way.Length);

        for (var run = 0; run < runs; run++)
        {
            // New bytes each time: a comment after the end of the file.
            var pdf = bomb.Concat(System.Text.Encoding.ASCII.GetBytes($"\n% {random.NextInt64():X16} {Guid.NewGuid():N}\n")).ToArray();
            var name = way.Contains("leave") ? $"doc-{run}.pdf" : "doc.pdf";
            var path = Path.Combine(inDirectory, name);
            var clock = Stopwatch.StartNew();
            var steps = new List<string>();
            void Lap(string step)
            {
                steps.Add($"{step} {clock.Elapsed.TotalMilliseconds:F0} ms");
                clock.Restart();
            }

            FileStream? held = null;
            if (way.StartsWith("hold open", StringComparison.Ordinal))
            {
                held = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.DeleteOnClose);
                held.Write(pdf);
                held.Flush(flushToDisk: false);
                Lap("write, held open");
            }
            else
            {
                File.WriteAllBytes(path, pdf);
                Lap("write, closed");
            }

            if (way.Contains("without one", StringComparison.Ordinal))
            {
                // No sandbox at all: only the file system and the clock.
                Thread.Sleep(TimeSpan.FromSeconds(7));
                Lap("wait");
            }
            else
            {
                var outcome = host.Run(module, new WasmHostRunRequest
                {
                    Argv = ["pdftext", $"/in/{name}", "/out"],
                    Preopens = [new WasmHostPreopen(inDirectory, "/in"), new WasmHostPreopen(outDirectory, "/out", Writable: true)],
                    CaptureDirectory = Path.Combine(root, "pdf-io"),
                    Fuel = PdfStageLimits.FUEL,
                    FuelAfterInstantiation = true,
                    MemoryBytes = PdfStageLimits.MEMORY_BYTES,
                    WallMs = PdfStageLimits.WALL_MS,
                    RandomSeed = PdfStageLimits.RANDOM_SEED,
                    StdoutLimitBytes = 4096,
                    StderrLimitBytes = 65536
                });
                Lap($"run ({outcome.Verdict}, fuel {outcome.FuelConsumed:N0})");
            }

            if (held != null)
            {
                held.Dispose();
                Lap("close (deletes)");
            }
            else if (way.EndsWith("delete", StringComparison.Ordinal))
            {
                File.Delete(path);
                Lap("delete");
            }

            TestContext.Out.WriteLine($"H9| {way} run {run}: {string.Join(", ", steps)}; file left: {File.Exists(path)}");
        }

        var end = Stopwatch.StartNew();
        try
        {
            Directory.Delete(root, recursive: true);
            TestContext.Out.WriteLine($"H9| {way}: folder deleted in {end.Elapsed.TotalMilliseconds:F0} ms");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"H9| {way}: folder not deleted after {end.Elapsed.TotalMilliseconds:F0} ms ({exception.GetType().Name})");
        }
    }
}
