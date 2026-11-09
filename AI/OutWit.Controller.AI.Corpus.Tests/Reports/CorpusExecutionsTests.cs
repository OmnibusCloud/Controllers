using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>The execution that stands for a shard is chosen by rule, never by arrival order.</summary>
[TestFixture]
public sealed class CorpusExecutionsTests
{
    private static CorpusResultData Result(int index, string content, CorpusShardVerdict verdict = CorpusShardVerdict.Completed, int documentsOut = 10)
    {
        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = verdict,
            ContentSha256 = content,
            Stats = new CorpusShardStatsData { DocumentsOut = documentsOut }
        };
    }

    [Test]
    public void OneExecutionPerShardInTaskOrderTest()
    {
        var canonical = CorpusExecutions.Canonical([Result(2, "c"), Result(0, "a"), Result(2, "c"), Result(1, "b")]);

        Assert.That(canonical.Select(result => result.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void ACompletedExecutionStandsForTheShardTest()
    {
        var failed = Result(0, "", CorpusShardVerdict.FetchFailed);
        var completed = Result(0, "zzz");

        Assert.That(CorpusExecutions.Pick([failed, completed]), Is.SameAs(completed));
        Assert.That(CorpusExecutions.Pick([completed, failed]), Is.SameAs(completed));
    }

    [Test]
    public void TheChoiceDoesNotDependOnArrivalTest()
    {
        var low = Result(0, "aaa");
        var high = Result(0, "bbb");

        Assert.That(CorpusExecutions.Pick([low, high]), Is.SameAs(low));
        Assert.That(CorpusExecutions.Pick([high, low]), Is.SameAs(low));
    }

    [Test]
    public void AShardWithOnlyFailedExecutionsStillHasAnEntryTest()
    {
        var canonical = CorpusExecutions.Canonical([Result(0, "", CorpusShardVerdict.FetchFailed), Result(0, "", CorpusShardVerdict.Oversized)]);

        Assert.That(canonical, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheReportNamesNoMachineTest()
    {
        var result = Result(0, "a");
        result.NodeId = Guid.NewGuid();
        result.NodeName = "contributors-laptop";

        var json = System.Text.Encoding.UTF8.GetString(CorpusReportWriter.Write([result]));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain(result.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("\"node"));
        });
    }

    [Test]
    public void TheReportCountsAShardExecutedTwiceOnceTest()
    {
        using var document = JsonDocument.Parse(CorpusReportWriter.Write([Result(0, "a", documentsOut: 7), Result(0, "a", documentsOut: 7), Result(1, "b", documentsOut: 5)]));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("documents_out").GetInt64(), Is.EqualTo(12));
            Assert.That(document.RootElement.GetProperty("shards").GetArrayLength(), Is.EqualTo(2));
        });
    }
}
