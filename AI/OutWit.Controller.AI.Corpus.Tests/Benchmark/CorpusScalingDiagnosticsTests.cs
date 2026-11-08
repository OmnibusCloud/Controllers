using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Benchmark;

/// <summary>
/// A diagnostic for parallel scaling (Explicit: run by hand): each stage of the pipeline alone,
/// on one thread and on many, over the reference shard's documents — the stage that does not
/// scale is the one that shares something.
/// </summary>
[TestFixture]
[Explicit("Scaling diagnostic: prints per-stage throughput at 1 and N threads")]
public sealed class CorpusScalingDiagnosticsTests
{
    [Test]
    public void PrintStageScalingTest()
    {
        var shard = CorpusBenchmarkShard.Bytes;
        var documents = CorpusBenchmarkShard.Documents().Select(d => d.Text).ToList();
        var jsonl = Encoding.UTF8.GetBytes(string.Join('\n', documents));
        var threads = Math.Min(16, Environment.ProcessorCount);

        var stages = new (string Name, Action Work)[]
        {
            ("read (gzip + WARC)", () => { using var input = new MemoryStream(shard); using var reader = new WarcReader(input, ownsStream: false); foreach (var _ in reader.ReadRecords()) { } }),
            ("lines + judge", () => { foreach (var text in documents) QualityFilter.Judge(LineCleaner.Clean(text, QualityRules.Spaced, out _), QualityRules.Spaced); }),
            ("minhash", () => { foreach (var text in documents) MinHashSigner.Sign(text); }),
            ("zstd stream", () => { using var sink = new MemoryStream(); using (var z = CorpusCompressor.CompressTo(sink)) z.Write(jsonl); }),
            ("full processor", () => CorpusBenchmark.ProcessReference(shard))
        };

        foreach (var (name, work) in stages)
        {
            var one = Time(work, 1);
            var many = Time(work, threads);
            TestContext.Out.WriteLine($"{name,-20} 1 thread {one.TotalMilliseconds,7:F0} ms | {threads} threads {many.TotalMilliseconds,7:F0} ms | speedup {threads * one.TotalSeconds / many.TotalSeconds,5:F1}x");
        }
    }

    [Test]
    public void PrintRealStageProfileTest()
    {
        var fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
        var golden = File.ReadAllBytes(Path.Combine(fixtures, "cc-main-2026-43-00000-head.warc.wet.gz"));
        var model = OutWit.Controller.AI.Corpus.Language.LanguageModel.Load(Path.Combine(fixtures, "language-model-cc-2026-43-16.bin"));
        var identifier = new OutWit.Controller.AI.Corpus.Language.LanguageIdentifier(model);

        var records = new List<string>();
        using (var input = new MemoryStream(golden))
        using (var reader = new WarcReader(input, ownsStream: false))
        {
            foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
                records.Add(Encoding.UTF8.GetString(record.Payload.Span));
        }

        var texts = records.Select(OutWit.Controller.AI.Corpus.Text.HtmlTextExtractor.CollapseLines).ToList();
        var cleaned = texts.Select(t => LineCleaner.Clean(t, QualityRules.Spaced, out _)).ToList();
        var lines = Encoding.UTF8.GetBytes(string.Join('\n', cleaned));

        var stages = new (string Name, Action Work)[]
        {
            ("read (gzip + WARC)", () => { using var input = new MemoryStream(golden); using var reader = new WarcReader(input, ownsStream: false); foreach (var _ in reader.ReadRecords()) { } }),
            ("collapse lines", () => { foreach (var r in records) OutWit.Controller.AI.Corpus.Text.HtmlTextExtractor.CollapseLines(r); }),
            ("language", () => { foreach (var t in texts) identifier.Identify(t); }),
            ("clean lines", () => { foreach (var t in texts) LineCleaner.Clean(t, QualityRules.Spaced, out _); }),
            ("judge", () => { foreach (var t in cleaned) QualityFilter.Judge(t, QualityRules.Spaced); }),
            ("minhash", () => { foreach (var t in cleaned) MinHashSigner.Sign(t); }),
            ("zstd", () => { using var sink = new MemoryStream(); using (var z = CorpusCompressor.CompressTo(sink)) z.Write(lines); })
        };

        foreach (var (name, work) in stages)
            TestContext.Out.WriteLine($"{name,-20} {Time(work, 1).TotalMilliseconds,7:F0} ms");
    }

    private static TimeSpan Time(Action work, int threads)
    {
        work();
        var runs = new List<TimeSpan>();
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() => work())).ToList();
            var clock = Stopwatch.StartNew();
            workers.ForEach(worker => worker.Start());
            workers.ForEach(worker => worker.Join());
            runs.Add(clock.Elapsed);
        }

        return CorpusBenchmark.Median(runs);
    }
}
