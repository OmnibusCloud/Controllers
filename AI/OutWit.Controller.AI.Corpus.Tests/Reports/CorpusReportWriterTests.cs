using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>The report: every shard of the task list in task order whatever order the results arrived in, the aggregate summed over the accepted ones, failures with their code.</summary>
[TestFixture]
public sealed class CorpusReportWriterTests
{
    [Test]
    public void ShardsAreOrderedAndSummedTest()
    {
        var tasks = CorpusTestRun.Tasks(3, CorpusTestRun.Labelled());
        var results = new object?[]
        {
            CorpusTestRun.Completed(tasks[2], documentsOut: 30, languages: [("eng", 20), ("deu", 10)], drops: [("min_words", 5)]),
            CorpusTestRun.Verdict(tasks[1], CorpusShardVerdict.FetchFailed, "http_503"),
            CorpusTestRun.Completed(tasks[0], documentsOut: 70, languages: [("eng", 70)], drops: [("min_words", 7), ("language_unknown", 3)])
        };

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, results)));
        var root = report.RootElement;
        var shards = root.GetProperty("shards").EnumerateArray().ToList();
        var summary = root.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("corpus-report/3"));
            Assert.That(root.GetProperty("controller").GetString(), Is.EqualTo(CorpusTestRun.VERSION));
            Assert.That(shards.Select(s => s.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(shards.Select(s => s.GetProperty("url").GetString()), Is.EqualTo(tasks.Select(task => task.Source!.Url)));
            Assert.That(shards[1].GetProperty("verdict").GetString(), Is.EqualTo("FetchFailed"));
            Assert.That(shards[1].GetProperty("error").GetString(), Is.EqualTo("http_503"));
            Assert.That(shards[1].GetProperty("stats").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(shards[0].GetProperty("text_blob").GetString(), Is.Not.Empty);
            Assert.That(shards.Select(s => s.GetProperty("executions").GetInt32()), Is.All.EqualTo(1), "each rests on one machine's word");
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("single_execution").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("verdicts").GetProperty("Completed").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("verdicts").GetProperty("FetchFailed").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.Zero);
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
        var tasks = CorpusTestRun.Tasks(3);
        var first = CorpusTestRun.Completed(tasks[0], drops: [("pdf_encrypted", 1)]);
        first.Stats!.Pdf = new CorpusPdfStatsData { In = 4, Truncated = 1, Extracted = 3, Pages = 30, Documents = 5, PagesCut = 1, RunningLines = 60, Fuel = 1_000_000 };
        var second = CorpusTestRun.Completed(tasks[1], documentsOut: 2);
        second.Stats!.Pdf = new CorpusPdfStatsData { In = 1, Extracted = 1, Pages = 2, Documents = 1, Fuel = 500 };
        var without = CorpusTestRun.Completed(tasks[2], documentsOut: 9);

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [first, second, without])));
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
        var tasks = CorpusTestRun.Tasks(1);
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0])])));

        Assert.That(report.RootElement.GetProperty("summary").TryGetProperty("pdf_in", out _), Is.False);
    }

    [Test]
    public void TheUnsupportedBreakdownIsReportedPerShardAndSummedTest()
    {
        var tasks = CorpusTestRun.Tasks(2, CorpusTestRun.Labelled());
        var first = CorpusTestRun.Completed(tasks[0], documentsOut: 10, drops: [("language_unsupported", 3)]);
        first.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("past_limit:spa", 1), ("swe", 2)]);
        var second = CorpusTestRun.Completed(tasks[1], documentsOut: 5, languages: [("deu", 5)], drops: [("language_unsupported", 4)]);
        second.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("dan", 1), ("swe", 3)]);

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [first, second])));
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
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger([], [])));

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.Zero);
            Assert.That(report.RootElement.GetProperty("shards").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public void TheReportNamesNoMachineTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var result = CorpusTestRun.Completed(tasks[0]);
        result.NodeId = Guid.NewGuid();
        result.NodeName = "contributors-laptop";

        var json = Encoding.UTF8.GetString(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [result])));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain(result.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("\"node"));
        });
    }

    [Test]
    public void AShardExecutedTwiceIsCountedOnceAndSaysItRestsOnTwoTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        using var document = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks,
            [CorpusTestRun.Completed(tasks[0], documentsOut: 7), CorpusTestRun.Completed(tasks[0], documentsOut: 7), CorpusTestRun.Completed(tasks[1], 'b', documentsOut: 5)])));
        var summary = document.RootElement.GetProperty("summary");
        var shards = document.RootElement.GetProperty("shards").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("documents_out").GetInt64(), Is.EqualTo(12));
            Assert.That(summary.GetProperty("single_execution").GetInt32(), Is.EqualTo(1));
            Assert.That(shards.Select(shard => shard.GetProperty("executions").GetInt32()), Is.EqualTo(new[] { 2, 1 }));
        });
    }

    [Test]
    public void AShardNothingCameBackForIsStillInTheReportTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        using var document = JsonDocument.Parse(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[1])])));
        var missing = document.RootElement.GetProperty("shards")[0];

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.EqualTo(2));
            Assert.That(missing.GetProperty("verdict").GetString(), Is.EqualTo("NoResult"));
            Assert.That(missing.GetProperty("reason").GetString(), Is.EqualTo("missing"));
            Assert.That(missing.GetProperty("executions").GetInt32(), Is.Zero);
            Assert.That(missing.GetProperty("url").GetString(), Is.EqualTo(tasks[0].Source!.Url));
        });
    }
}
