using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>The integrity document: executions paired by index, same-node pairs counted not compared, mismatches named by field.</summary>
[TestFixture]
public sealed class CorpusIntegrityWriterTests
{
    [Test]
    public void PairsAreComparedAcrossNodesOnlyTest()
    {
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();

        var executions = new List<CorpusResultData>
        {
            Result(0, nodeA, "aaa", 10),
            Result(2, nodeB, "ccc", 30),
            Result(1, nodeA, "bbb", 20),
            Result(3, nodeA, "ddd", 40),
            Result(2, nodeA, "ccc", 30),
            Result(1, nodeA, "bbb", 20),
            Result(0, nodeB, "aaa", 11)
        };

        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(executions));
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo(CorpusIntegrityWriter.SCHEMA));
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(3), "the shard executed once makes no pair");
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("same_node").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("unpaired").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(0.5));

            Assert.That(pairs.Select(p => p.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(pairs[0].GetProperty("compared").GetBoolean(), Is.True);
            Assert.That(pairs[0].GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pairs[0].GetProperty("differs").EnumerateArray().Select(d => d.GetString()), Is.EqualTo(new[] { "documents" }));
            Assert.That(pairs[1].GetProperty("compared").GetBoolean(), Is.False, "same node");
            Assert.That(pairs[2].GetProperty("match").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void AShardExecutedThreeTimesMakesTwoPairsTest()
    {
        var executions = new List<CorpusResultData>
        {
            Result(0, Guid.NewGuid(), "aaa", 10),
            Result(0, Guid.NewGuid(), "aaa", 10),
            Result(0, Guid.NewGuid(), "bbb", 10)
        };

        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(executions));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void TheOrderOfTheExecutionsDoesNotChangeTheDocumentTest()
    {
        var first = Result(0, Guid.NewGuid(), "aaa", 10);
        var second = Result(0, Guid.NewGuid(), "bbb", 12);

        Assert.That(CorpusIntegrityWriter.Write([first, second]), Is.EqualTo(CorpusIntegrityWriter.Write([second, first])));
    }

    [Test]
    public void VersionSkewIsCountedNotComparedTest()
    {
        var primary = Result(0, Guid.NewGuid(), "aaa", 10);
        primary.ControllerVersion = "0.1.4";
        var replica = Result(0, Guid.NewGuid(), "bbb", 12);
        replica.ControllerVersion = "0.1.5";

        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write([primary, replica]));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("version_skew").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero, "a different product from a different controller is not a mismatch");
            Assert.That(pair.GetProperty("compared").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("replica").GetProperty("controller").GetString(), Is.EqualTo("0.1.5"));
        });
    }

    [Test]
    public void DifferencesNameEveryFieldTest()
    {
        var primary = Result(0, Guid.NewGuid(), "aaa", 10);
        primary.Stats!.Drops = [new CorpusCountData { Name = "min_words", Count = 3 }];
        primary.Stats.Languages = [new CorpusCountData { Name = "eng", Count = 10 }];

        var replica = Result(0, Guid.NewGuid(), "bbb", 9);
        replica.Verdict = CorpusShardVerdict.FetchFailed;
        replica.Stats!.Drops = [new CorpusCountData { Name = "min_words", Count = 4 }];
        replica.Stats.Languages = [new CorpusCountData { Name = "deu", Count = 9 }];

        Assert.That(CorpusIntegrityWriter.Differences(primary, replica), Is.EqualTo(new[] { "verdict", "content", "documents", "drops", "languages" }));
        Assert.That(CorpusIntegrityWriter.Differences(primary, primary.Clone()), Is.Empty);
    }

    [Test]
    public void ShardsExecutedOnceAreAnEmptyDocumentTest()
    {
        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write([Result(0, Guid.NewGuid(), "aaa", 1), Result(1, Guid.NewGuid(), "bbb", 1)]));

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("pairs").GetArrayLength(), Is.Zero);
        });
    }

    private static CorpusResultData Result(int index, Guid node, string content, int documentsOut)
    {
        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = CorpusShardVerdict.Completed,
            NodeId = node,
            NodeName = "node",
            ContentSha256 = content,
            Stats = new CorpusShardStatsData { RecordsIn = 100, DocumentsExtracted = 50, DocumentsOut = documentsOut }
        };
    }
}
