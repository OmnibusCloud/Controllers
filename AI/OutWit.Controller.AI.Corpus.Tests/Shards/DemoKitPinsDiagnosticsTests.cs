using System.Globalization;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The demo kit's pins against the stages as they are now: the kit lists, for each of its real
/// Common Crawl WET files, the content hash, the signatures hash and the counters a run gives
/// (<c>demo/01-common-crawl/shards.tsv</c>). A release that changes a stage moves them; a release
/// that should not have is checked here. The files are third-party content and are not in the
/// repository: the test reads their paths, one per line, from the file the environment variable
/// <c>WITAI_CORPUS_KIT_SHARDS</c> names, and runs only when asked for
/// (<c>--filter Category=Diagnostics</c>). Minutes of every core.
/// </summary>
[TestFixture]
[Explicit("Diagnostics: runs the demo kit's real shards through the stages and compares them with the kit's pins; needs the files.")]
[Category("Diagnostics")]
[NonParallelizable]
public sealed class DemoKitPinsDiagnosticsTests
{
    #region Constants

    private const string LIST_VARIABLE = "WITAI_CORPUS_KIT_SHARDS";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    #endregion

    #region Tests

    [Test]
    public void TheKitsShardsGiveTheKitsPinsTest()
    {
        var list = Environment.GetEnvironmentVariable(LIST_VARIABLE);
        if (string.IsNullOrEmpty(list) || !File.Exists(list))
            Assert.Ignore($"{LIST_VARIABLE} names no file of shard paths");

        var pins = Pins();
        var files = File.ReadAllLines(list!).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        var model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE));
        var lines = new string?[files.Count];
        var moved = 0;

        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, index =>
        {
            var name = Path.GetFileName(files[index]);
            if (!pins.TryGetValue(name, out var pin))
            {
                lines[index] = $"KIT| {name}: not a file of the kit";
                return;
            }

            // The kit's pipeline: every stage at its default, the reference pack.
            var processor = new ShardProcessor(new CorpusPipelineData { Version = "kit" }, model);
            using var shard = new FileStream(files[index], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var text = new MemoryStream();
            using var signatures = new MemoryStream();
            var product = processor.Process(shard, CorpusSourceFormat.Wet, text, signatures);
            var stats = product.Stats.Build(0, text.Length, 0, 0);

            var now = (product.ContentSha256, product.SignaturesSha256, stats.RecordsIn, stats.DocumentsExtracted, stats.DocumentsOut, stats.BytesProduced);
            if (now == pin)
            {
                lines[index] = $"KIT| {name}: as pinned ({stats.DocumentsOut} documents)";
                return;
            }

            Interlocked.Increment(ref moved);
            lines[index] = $"KIT| {name}: MOVED from {pin} to {now}; drops {string.Join(", ", stats.Drops.Select(drop => $"{drop.Name} {drop.Count}"))}";
        });

        foreach (var line in lines)
            TestContext.Out.WriteLine(line);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.Zero, "shards whose product is not the kit's");
            Assert.That(lines.Count(line => line != null && line.Contains("as pinned", StringComparison.Ordinal)), Is.EqualTo(pins.Count), "every file of the kit was run");
        });
    }

    #endregion

    #region Tools

    private static Dictionary<string, (string Content, string Signatures, long Records, long Extracted, long Out, long Produced)> Pins()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OutWit.slnx")))
            dir = Path.GetDirectoryName(dir);

        var path = Path.Combine(dir!, "AI", "OutWit.Controller.AI.Corpus.Live", "demo", "01-common-crawl", "shards.tsv");
        return File.ReadAllLines(path).Skip(1).Where(line => line.Length > 0).Select(line => line.Split('\t')).ToDictionary(
            columns => columns[1],
            columns => (columns[4], columns[5], Number(columns[6]), Number(columns[7]), Number(columns[8]), Number(columns[9])),
            StringComparer.Ordinal);
    }

    private static long Number(string text)
    {
        return long.Parse(text, CultureInfo.InvariantCulture);
    }

    #endregion
}
