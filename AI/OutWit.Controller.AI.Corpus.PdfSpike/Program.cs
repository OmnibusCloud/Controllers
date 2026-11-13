using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.PdfSpike.Cc;
using OutWit.Controller.AI.Corpus.PdfSpike.Hostile;
using OutWit.Controller.AI.Corpus.PdfSpike.Runner;
using OutWit.Controller.AI.Corpus.PdfSpike.Salvage;

namespace OutWit.Controller.AI.Corpus.PdfSpike;

/// <summary>
/// pdf-spike gen-hostile &lt;dir&gt;
/// pdf-spike fetch-cc &lt;cc-pdfs.json&gt; &lt;dir&gt;
/// pdf-spike salvage &lt;cc-pdfs.json&gt; &lt;dir&gt; &lt;out-dir&gt;   (the documents the list marks truncated)
/// pdf-spike batch &lt;engine&gt; &lt;dir&gt; &lt;out.jsonl&gt; [--timeout-s N] [--mem-mb N] [--max-pages N] [--stack BYTES] [--texts DIR] [--culture NAME]
/// pdf-spike inproc &lt;engine&gt; &lt;dir&gt; &lt;out.jsonl&gt; [--max-pages N]
/// pdf-spike run-one &lt;engine&gt; &lt;file&gt; [--max-pages N] [--stack BYTES] [--text PATH] [--culture NAME]
/// Engines: pdfpig, pdfpig-budget, pdflibrary.
/// </summary>
public static class Program
{
    #region Functions

    /// <summary>The entry point.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: pdf-spike gen-hostile|fetch-cc|batch|inproc|run-one ...");
            return 2;
        }

        switch (args[0])
        {
            case "gen-hostile":
                PdfHostileSet.Write(args[1]);
                return 0;

            case "fetch-cc":
                return await CcPdfFetcher.FetchAsync(args[1], args[2]) == 0 ? 0 : 1;

            case "batch":
            {
                var runner = new PdfBatchRunner(
                    args[1],
                    TimeSpan.FromSeconds(Option(args, "--timeout-s", 60)),
                    (long)Option(args, "--mem-mb", 4096) << 20,
                    Option(args, "--max-pages", 0),
                    Option(args, "--stack", 0),
                    OptionText(args, "--texts"),
                    OptionText(args, "--culture") ?? "");
                runner.RunFolder(args[2], args[3]);
                return 0;
            }

            case "salvage":
                return SalvageTruncated(args[1], args[2], args[3]);

            case "inproc":
                return InProcess(args[1], args[2], args[3], Option(args, "--max-pages", 0));

            case "run-one":
            {
                var culture = OptionText(args, "--culture");
                if (culture != null)
                {
                    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(culture);
                    CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(culture);
                }

                var record = PdfEngineRun.Run(args[1], args[2], Option(args, "--max-pages", 0), Option(args, "--stack", 0), OptionText(args, "--text"));
                Console.Out.WriteLine("RESULT " + JsonSerializer.Serialize(record));
                return 0;
            }

            default:
                Console.Error.WriteLine($"unknown command '{args[0]}'");
                return 2;
        }
    }

    private static int SalvageTruncated(string listPath, string folder, string output)
    {
        Directory.CreateDirectory(output);
        using var list = JsonDocument.Parse(File.ReadAllText(listPath));
        foreach (var entry in list.RootElement.GetProperty("documents").EnumerateArray())
        {
            if (!entry.GetProperty("truncated").GetBoolean())
                continue;

            var name = entry.GetProperty("name").GetString()!;
            var repaired = PdfSalvage.Repair(File.ReadAllBytes(Path.Combine(folder, name)));
            Console.Error.WriteLine($"  {name}: {(repaired == null ? "no complete page" : $"{repaired.Length:N0} bytes")}");
            if (repaired != null)
                File.WriteAllBytes(Path.Combine(output, name), repaired);
        }

        return 0;
    }

    private static int InProcess(string engine, string folder, string output, int maxPages)
    {
        var files = Directory.GetFiles(folder, "*.pdf").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        using var writer = new StreamWriter(output);
        var clock = Stopwatch.StartNew();
        long bytes = 0;
        var ok = 0;

        foreach (var file in files)
        {
            var record = PdfEngineRun.Run(engine, file, maxPages, 0, null);
            bytes += record.Bytes;
            ok += record.Verdict == "ok" ? 1 : 0;
            writer.WriteLine(JsonSerializer.Serialize(record));
        }

        clock.Stop();
        using var self = Process.GetCurrentProcess();
        Console.WriteLine($"{engine}: {files.Length} documents, {ok} ok, {bytes / 1048576.0:0.0} MB in {clock.Elapsed.TotalSeconds:0.0} s = {bytes / 1048576.0 / clock.Elapsed.TotalSeconds:0.00} MB/s, {files.Length / clock.Elapsed.TotalSeconds:0.0} docs/s; process peak {self.PeakWorkingSet64 / 1048576.0:0} MB");
        return 0;
    }

    private static int Option(string[] args, string name, int fallback)
    {
        var text = OptionText(args, name);
        return text == null ? fallback : int.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string? OptionText(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    #endregion
}
