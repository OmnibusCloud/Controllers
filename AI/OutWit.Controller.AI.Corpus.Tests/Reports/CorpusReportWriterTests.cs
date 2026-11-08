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
