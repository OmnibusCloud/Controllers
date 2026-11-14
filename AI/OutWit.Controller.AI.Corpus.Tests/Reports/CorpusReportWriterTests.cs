using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>The report: shards in task order whatever order they arrived in, the aggregate summed over them, failures with their reason.</summary>
[TestFixture]
public sealed class CorpusReportWriterTests
{
    [Test]
    public void ShardsAreOrderedAndSummedTest()
    {
        var results = new List<CorpusResultData>
        {
            Completed(2, documentsOut: 30, languages: [("eng", 20), ("deu", 10)], drops: [("min_words", 5)]),
            new() { TaskIndex = 1, Verdict = CorpusShardVerdict.FetchFailed, Error = "503", SourceUrl = "https://h/b.warc.gz" },
            Completed(0, documentsOut: 70, languages: [("eng", 70)], drops: [("min_words", 7), ("language_unknown", 3)])
        };

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(results));
        var root = report.RootElement;
        var shards = root.GetProperty("shards").EnumerateArray().ToList();
        var summary = root.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo(CorpusReportWriter.SCHEMA));
            Assert.That(shards.Select(s => s.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(shards[1].GetProperty("verdict").GetString(), Is.EqualTo("FetchFailed"));
            Assert.That(shards[1].GetProperty("error").GetString(), Is.EqualTo("503"));
            Assert.That(shards[1].GetProperty("stats").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(shards[0].GetProperty("text_blob").GetString(), Is.Not.Empty);
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("verdicts").GetProperty("Completed").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("verdicts").GetProperty("FetchFailed").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("documents_out").GetInt64(), Is.EqualTo(100));
            Assert.That(summary.GetProperty("languages").GetProperty("eng").GetInt64(), Is.EqualTo(90));
            Assert.That(summary.GetProperty("languages").GetProperty("deu").GetInt64(), Is.EqualTo(10));
            Assert.That(summary.GetProperty("drops").GetProperty("min_words").GetInt64(), Is.EqualTo(12));
            Assert.That(summary.GetProperty("drops").GetProperty("language_unknown").GetInt64(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("languages").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "deu", "eng" }), "ordinal order");
        });
    }

    [Test]
    public void ThePdfCountsAreWrittenWhereAShardMetAPdfAndSummedTest()
    {
        var first = Completed(0, documentsOut: 5, languages: [("eng", 5)], drops: [("pdf_encrypted", 1)]);
        first.Stats!.Pdf = new CorpusPdfStatsData { In = 4, Truncated = 1, Extracted = 3, Pages = 30, Documents = 5, PagesCut = 1, RunningLines = 60, Fuel = 1_000_000 };
        var second = Completed(1, documentsOut: 2, languages: [("eng", 2)], drops: []);
        second.Stats!.Pdf = new CorpusPdfStatsData { In = 1, Extracted = 1, Pages = 2, Documents = 1, Fuel = 500 };
        var without = Completed(2, documentsOut: 9, languages: [("eng", 9)], drops: []);

        using var report = JsonDocument.Parse(CorpusReportWriter.Write([first, second, without]));
        var summary = report.RootElement.GetProperty("summary");
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("pdf_in").GetInt64(), Is.EqualTo(5));
            Assert.That(summary.GetProperty("pdf_truncated").GetInt64(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("pdf_extracted").GetInt64(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("pdf_pages").GetInt64(), Is.EqualTo(32));
            Assert.That(summary.GetProperty("pdf_documents").GetInt64(), Is.EqualTo(6));
            Assert.That(summary.GetProperty("pdf_pages_cut").GetInt64(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("pdf_running_lines").GetInt64(), Is.EqualTo(60));
            Assert.That(summary.GetProperty("pdf_fuel").GetInt64(), Is.EqualTo(1_000_500));
            Assert.That(summary.GetProperty("drops").GetProperty("pdf_encrypted").GetInt64(), Is.EqualTo(1));
            Assert.That(shards[0].GetProperty("stats").GetProperty("pdf_in").GetInt32(), Is.EqualTo(4));
            Assert.That(shards[2].GetProperty("stats").TryGetProperty("pdf_in", out _), Is.False, "a shard that met no PDF says nothing of them");
        });
    }

    [Test]
    public void AReportWithoutPdfsHasNoPdfCountsTest()
    {
        using var report = JsonDocument.Parse(CorpusReportWriter.Write([Completed(0, documentsOut: 1, languages: [("eng", 1)], drops: [])]));

        Assert.That(report.RootElement.GetProperty("summary").TryGetProperty("pdf_in", out _), Is.False);
    }

    [Test]
    public void TheUnsupportedBreakdownIsReportedPerShardAndSummedTest()
    {
        var first = Completed(0, documentsOut: 10, languages: [("eng", 10)], drops: [("language_unsupported", 3)]);
        first.Stats!.UnsupportedLanguages = [new CorpusCountData { Name = "past_limit:spa", Count = 1 }, new CorpusCountData { Name = "swe", Count = 2 }];
        var second = Completed(1, documentsOut: 5, languages: [("deu", 5)], drops: [("language_unsupported", 4)]);
        second.Stats!.UnsupportedLanguages = [new CorpusCountData { Name = "dan", Count = 1 }, new CorpusCountData { Name = "swe", Count = 3 }];

        using var report = JsonDocument.Parse(CorpusReportWriter.Write([first, second]));
        var summary = report.RootElement.GetProperty("summary").GetProperty("unsupported_languages");
        var shard = report.RootElement.GetProperty("shards")[0].GetProperty("stats").GetProperty("unsupported_languages");

        Assert.Multiple(() =>
        {
            Assert.That(summary.EnumerateObject().Select(p => (p.Name, p.Value.GetInt64())), Is.EqualTo(new[] { ("dan", 1L), ("past_limit:spa", 1L), ("swe", 5L) }));
            Assert.That(shard.EnumerateObject().Select(p => (p.Name, p.Value.GetInt64())), Is.EqualTo(new[] { ("past_limit:spa", 1L), ("swe", 2L) }));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("languages").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "deu", "eng" }), "the language histogram is the documents out, untouched");
        });
    }

    [Test]
    public void EmptyRunIsAValidReportTest()
    {
        using var report = JsonDocument.Parse(CorpusReportWriter.Write([]));

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.Zero);
            Assert.That(report.RootElement.GetProperty("shards").GetArrayLength(), Is.Zero);
        });
    }

    private static CorpusResultData Completed(int index, int documentsOut, (string, int)[] languages, (string, int)[] drops)
    {
        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = CorpusShardVerdict.Completed,
            SourceUrl = $"https://h/{index}.warc.gz",
            TextBlobId = Guid.NewGuid(),
            SignaturesBlobId = Guid.NewGuid(),
            ContentSha256 = new string('a', 64),
            NodeId = Guid.NewGuid(),
            Stats = new CorpusShardStatsData
            {
                RecordsIn = 100,
                DocumentsExtracted = documentsOut + drops.Sum(d => d.Item2),
                DocumentsOut = documentsOut,
                Languages = languages.Select(l => new CorpusCountData { Name = l.Item1, Count = l.Item2 }).ToList(),
                Drops = drops.Select(d => new CorpusCountData { Name = d.Item1, Count = d.Item2 }).ToList(),
                BytesFetched = 1000,
                BytesProduced = 300
            }
        };
    }
}
