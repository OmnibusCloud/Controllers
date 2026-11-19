using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.Grid.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// A fan-out that came back short keeps its checking, and says that it came back short. The
/// platform picks the packages that run twice and does not say which, so a first round whose
/// answers are lost leaves the server unable to tell what had been picked: every shard that goes
/// out again goes out on two machines when the run asked for sampling, the repair round is ruled
/// as the first, and both documents state the sampling that was asked for and how each round came
/// back. In every scenario 'a' is the honest result and '0' the forged one.
/// </summary>
[TestFixture]
public sealed class CorpusShortFanOutTests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Corpus";

    #endregion

    #region The repair round

    [Test]
    public void ARoundThatCameBackEmptyIsCheckedAgainAndALieInTheRepairIsOutvotedTest()
    {
        // The run asked for every shard on two machines, and the first round's answers never
        // reached the script. The repair round asked one machine for each shard: whatever that
        // machine said stood, unchecked, and one forged result was the product. Now two machines
        // answer each shard of it; one of them lies about shard 1.
        var tasks = CorpusTestRun.Tasks(4);
        var repairs = CorpusTestRun.Ledger(tasks, [], samplingRate: 1.0).RepairTasks();
        var repaired = repairs.SelectMany(task => new object?[] { CorpusTestRun.Completed(task, 'a'), CorpusTestRun.Completed(task, task.TaskIndex == 1 ? '0' : 'a') }).ToList();
        var disputes = CorpusTestRun.Ledger(tasks, [], (repairs, repaired), samplingRate: 1.0).DisputeTasks();

        var ledger = CorpusTestRun.Ledger(tasks, [], (repairs, repaired),
            (disputes, disputes.SelectMany(task => new object?[] { CorpusTestRun.Completed(task, 'a'), CorpusTestRun.Completed(task, '0'), CorpusTestRun.Completed(task, 'a') })), samplingRate: 1.0);
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var summary = integrity.RootElement.GetProperty("summary");
        var dispute = integrity.RootElement.GetProperty("disputes")[0];
        var pairs = integrity.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Select(task => task.TaskIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }), "every shard goes out again");
            Assert.That(disputes.Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }), "the copies of shard 1 disagree");

            Assert.That(shards.Select(shard => shard.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"));
            Assert.That(shards.Select(shard => shard.GetProperty("content_sha256").GetString()), Is.All.EqualTo(CorpusTestRun.Hash('a')), "no forged product is in the report");
            Assert.That(shards.Select(shard => shard.GetProperty("executions").GetInt32()), Is.All.EqualTo(2), "no shard rests on one machine");
            Assert.That(ledger.SingleExecutions, Is.Zero);
            Assert.That(ledger.RejectedResults, Is.Zero);

            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.Zero, "nothing of the first round was compared");
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("repaired").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("repair_pairs").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("repair_compared").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("repair_mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(1));

            Assert.That(pairs.Select(pair => pair.GetProperty("round").GetString()), Is.All.EqualTo("repair"));
            Assert.That(pairs.Select(pair => pair.GetProperty("match").GetBoolean()), Is.EqualTo(new[] { true, false, true, true }));
            Assert.That(pairs.Select(pair => pair.TryGetProperty("dispute", out _)), Is.EqualTo(new[] { false, true, false, false }));
            Assert.That(dispute.GetProperty("first_copies").GetInt32(), Is.Zero);
            Assert.That(dispute.GetProperty("repair_copies").GetInt32(), Is.EqualTo(2));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(dispute.GetProperty("accepted").GetProperty("content_sha256").GetString(), Is.EqualTo(CorpusTestRun.Hash('a')));
        });
    }

    [Test]
    public void MoreValuesThanTheRepairRoundSentOutAreADisputeTest()
    {
        // Two machines were asked and three values came back: one machine answered twice, and
        // nothing says which. As in the first round, three machines settle it.
        var tasks = CorpusTestRun.Tasks(1);
        var repairs = CorpusTestRun.Ledger(tasks, []).RepairTasks();

        var ledger = CorpusTestRun.Ledger(tasks, [], (repairs, [CorpusTestRun.Completed(repairs[0], '0'), CorpusTestRun.Completed(repairs[0], '0'), CorpusTestRun.Completed(repairs[0], '0')]));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted, Is.Null, "three values that agree are still more than were asked for");
            Assert.That(ledger.DisputeTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 0 }));
            Assert.That(ledger.RejectedResults, Is.Zero);
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("results").GetInt32(), Is.EqualTo(3));
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("dispute").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void ARepairAnsweredByOneMachineIsOneMachinesWordTest()
    {
        // A run that asked for no sampling repairs on one machine, and so does a pool of one.
        var tasks = CorpusTestRun.Tasks(1);
        var repairs = CorpusTestRun.Ledger(tasks, [], samplingRate: 0).RepairTasks();

        var ledger = CorpusTestRun.Ledger(tasks, [], (repairs, [CorpusTestRun.Completed(repairs[0], 'a')]), samplingRate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Copies, Is.EqualTo(1));
            Assert.That(ledger.SingleExecutions, Is.EqualTo(1));
            Assert.That(ledger.Repaired, Is.EqualTo(1));
            Assert.That(ledger.Sampling.State, Is.EqualTo(CorpusSampling.STATE_NOT_ASKED));
        });
    }

    [Test]
    public void TheRepairRoundGoesOutTwiceWhenTheRunSamplesAndOnceWhenItDoesNotTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (rate, share, again) in new[] { (0.0, 0.0, 0.0), (-1.0, 0.0, 0.0), (double.NaN, 0.0, 0.0), (0.0001, 0.0001, 1.0), (0.25, 0.25, 1.0), (1.0, 1.0, 1.0), (7.0, 1.0, 1.0) })
            {
                var options = new CorpusOptionsData { SamplingRate = rate };

                Assert.That(CorpusSampling.RateOf(options), Is.EqualTo(share), $"the share a rate of {rate} asks for");
                Assert.That(CorpusSampling.RepairRateOf(options), Is.EqualTo(again), $"the share of the repair round under a rate of {rate}");
            }

            Assert.That(CorpusSampling.RateOf(null), Is.Zero);
            Assert.That(CorpusSampling.RepairRateOf(null), Is.Zero);
            Assert.That(CorpusLedger.REPAIR_COPIES, Is.EqualTo(CorpusLedger.FIRST_ROUND_COPIES), "a shard that goes out again is checked as a sampled one is");
        });
    }

    #endregion

    #region What the documents state

    [Test]
    public void TheSamplingAskedForAndWhatCameBackAreSaidInAWordTest()
    {
        var tasks = CorpusTestRun.Tasks(4);
        object?[] Twice(int index) => [CorpusTestRun.Completed(tasks[index]), CorpusTestRun.Completed(tasks[index])];
        object?[] Once(int index) => [CorpusTestRun.Completed(tasks[index])];
        object?[] WithAFault(int index) => [CorpusTestRun.Completed(tasks[index]), CorpusTestRun.Fault(tasks[index])];

        (string Name, double? Rate, object?[] First, string State, int Asked, int Answered, int Compared)[] runs =
        [
            ("no sampling asked", 0, [.. Once(0), .. Once(1), .. Once(2), .. Once(3)], CorpusSampling.STATE_NOT_ASKED, 0, 0, 0),
            ("every package twice, as asked", 1, [.. Twice(0), .. Twice(1), .. Twice(2), .. Twice(3)], CorpusSampling.STATE_AS_ASKED, 4, 4, 4),
            ("half of them asked, half came back twice", 0.5, [.. Twice(0), .. Once(1), .. Twice(2), .. Once(3)], CorpusSampling.STATE_AS_ASKED, 2, 2, 2),
            ("a hundredth asked: one package at the least", 0.01,[.. Twice(0), .. Once(1), .. Once(2), .. Once(3)], CorpusSampling.STATE_AS_ASKED, 1, 1, 1),
            ("one second answer did not come back", 1, [.. Twice(0), .. Once(1), .. Twice(2), .. Twice(3)], CorpusSampling.STATE_SHORT, 4, 3, 3),
            ("one package was not answered at all", 1, [.. Twice(0), .. Twice(2), .. Twice(3)], CorpusSampling.STATE_SHORT, 4, 3, 3),
            ("every answer was lost", 1, [], CorpusSampling.STATE_NONE, 4, 0, 0),
            ("a pool of one machine: nothing ran twice", 0.5, [.. Once(0), .. Once(1), .. Once(2), .. Once(3)], CorpusSampling.STATE_NONE, 2, 0, 0),
            ("every second answer was a fault of its node", 1, [.. WithAFault(0), .. WithAFault(1), .. WithAFault(2), .. WithAFault(3)], CorpusSampling.STATE_NONE, 4, 4, 0),
            ("the rate is not known to the activity", null, [.. Twice(0), .. Once(1), .. Once(2), .. Once(3)], CorpusSampling.STATE_UNKNOWN, 0, 1, 1)
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, rate, first, state, asked, answered, compared) in runs)
            {
                var sampling = CorpusTestRun.Ledger(tasks, first, samplingRate: rate).Sampling;

                Assert.That(sampling.State, Is.EqualTo(state), name);
                Assert.That((sampling.Packages, sampling.AskedTwice, sampling.AnsweredTwice, sampling.Compared), Is.EqualTo((4, asked, answered, compared)), name);
                Assert.That(CorpusSampling.STATES, Does.Contain(sampling.State), name);
                Assert.That(sampling.Statement, Is.Not.Empty.And.Not.Contain("'"), $"{name}: a sentence that needs no escaping in a document");
            }

            Assert.That(CorpusTestRun.Ledger(tasks, [], samplingRate: 1).Sampling.Statement, Is.EqualTo("The run asked for 4 of its 4 package(s) to run on two machines, 0 came back twice and no shard was compared: the checking the run asked for did not happen in the first round."));
            Assert.That(CorpusTestRun.Ledger(tasks, [.. Twice(0), .. Once(1), .. Twice(2), .. Twice(3)], samplingRate: 1).Sampling.Statement, Is.EqualTo("The run asked for 4 of its 4 package(s) to run on two machines and 3 came back twice: the checking of the first round is short of what the run asked for."));
        });
    }

    [Test]
    public void ThePackagesAskedTwiceAreCountedByThePlatformsRuleTest()
    {
        // The platform picks the packages; the server states how many the rate comes to. The two
        // must round alike, or an honest run would read as short.
        double[] rates = [0, 0.0001, 0.01, 0.05, 0.1, 0.2, 0.25, 1.0 / 3, 0.5, 0.75, 0.99, 0.999, 1];

        Assert.Multiple(() =>
        {
            foreach (var rate in rates)
            {
                for (var packages = 1; packages <= 300; packages++)
                {
                    var planned = WitGridReplicaPlanner.Plan(packages, CorpusLedger.FIRST_ROUND_COPIES, rate, 2, _ => 0);
                    var picked = planned == null ? 0 : planned.Count - packages;
                    var asked = CorpusSampling.Of(rate, CorpusRoundAnswers.Of(Enumerable.Repeat(1, packages)), 0).AskedTwice;

                    if (asked != picked)
                        Assert.Fail($"{packages} package(s) at a rate of {rate}: the platform picks {picked}, the documents say {asked}");
                }
            }
        });
    }

    [Test]
    public void BothDocumentsStateTheSamplingAndTheRoundsInTheSameWordsTest()
    {
        // Shard 0 came back twice; shard 1 once; shard 2 not at all, and its repair came back
        // from one machine of the two; shard 3 came back as copies that disagree, and three
        // machines settled it.
        var tasks = CorpusTestRun.Tasks(4);
        var first = new object?[]
        {
            CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]),
            CorpusTestRun.Completed(tasks[1]),
            CorpusTestRun.Completed(tasks[3]), CorpusTestRun.Completed(tasks[3], '0')
        };
        var before = CorpusTestRun.Ledger(tasks, first, samplingRate: 0.75);
        var repairs = before.RepairTasks();
        var disputes = before.DisputeTasks();
        var ledger = CorpusTestRun.Ledger(tasks, first, (repairs, [CorpusTestRun.Completed(repairs[0])]),
            (disputes, [CorpusTestRun.Completed(disputes[0]), CorpusTestRun.Completed(disputes[0]), CorpusTestRun.Completed(disputes[0], '0')]), samplingRate: 0.75);

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        using var reportSchema = JsonDocument.Parse(CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "docs", "corpus-report.schema.json"));
        using var integritySchema = JsonDocument.Parse(CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "docs", "corpus-integrity.schema.json"));
        var sampling = integrity.RootElement.GetProperty("sampling");
        var rounds = integrity.RootElement.GetProperty("rounds");

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-report/5"));
            Assert.That(integrity.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-integrity/7"));
            Assert.That(CorpusSchemaCheck.Validate(reportSchema.RootElement, report.RootElement), Is.Empty);
            Assert.That(CorpusSchemaCheck.Validate(integritySchema.RootElement, integrity.RootElement), Is.Empty);
            Assert.That(report.RootElement.GetProperty("sampling").GetRawText(), Is.EqualTo(sampling.GetRawText()), "the report says of the sampling what the integrity document says");
            Assert.That(report.RootElement.GetProperty("rounds").GetRawText(), Is.EqualTo(rounds.GetRawText()));

            Assert.That(sampling.GetProperty("rate").GetDouble(), Is.EqualTo(0.75));
            Assert.That(sampling.GetProperty("state").GetString(), Is.EqualTo("short"));
            Assert.That(sampling.GetProperty("packages").GetInt32(), Is.EqualTo(4));
            Assert.That(sampling.GetProperty("asked_twice").GetInt32(), Is.EqualTo(3));
            Assert.That(sampling.GetProperty("answered_twice").GetInt32(), Is.EqualTo(2));
            Assert.That(sampling.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(sampling.GetProperty("compared").GetInt32(), Is.EqualTo(integrity.RootElement.GetProperty("summary").GetProperty("compared").GetInt32()));
            Assert.That(sampling.GetProperty("statement").GetString(), Is.EqualTo(ledger.Sampling.Statement));

            Assert.That(Round(rounds, "first"), Is.EqualTo((4, 5, 1, 1, 2, 0, 0)), "four packages, five answers: one unanswered, one answered once, two twice");
            Assert.That(Round(rounds, "repair"), Is.EqualTo((1, 1, 0, 1, 0, 0, 0)));
            Assert.That(Round(rounds, "dispute"), Is.EqualTo((1, 3, 0, 0, 0, 1, 0)));
        });
    }

    [Test]
    public void ADocumentWrittenWithoutTheOptionsSaysTheRateIsNotKnownTest()
    {
        // A script written for an earlier controller hands Corpus.Integrity no options.
        var tasks = CorpusTestRun.Tasks(1);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0])]);

        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        using var schema = JsonDocument.Parse(CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "docs", "corpus-integrity.schema.json"));
        var sampling = integrity.RootElement.GetProperty("sampling");

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, integrity.RootElement), Is.Empty);
            Assert.That(sampling.GetProperty("rate").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(sampling.GetProperty("state").GetString(), Is.EqualTo("unknown"));
            Assert.That(sampling.GetProperty("asked_twice").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public void ARoundThatWasNotRunHasNoPackagesTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1])], samplingRate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.FirstRound, Is.EqualTo(new CorpusRoundAnswers(2, 2, 0, 2, 0, 0, 0)));
            Assert.That(ledger.RepairRound, Is.EqualTo(CorpusRoundAnswers.NONE));
            Assert.That(ledger.DisputeRound, Is.EqualTo(CorpusRoundAnswers.NONE));
            Assert.That(CorpusRoundAnswers.Of([0, 1, 2, 3, 4, 9]), Is.EqualTo(new CorpusRoundAnswers(6, 19, 1, 1, 1, 1, 2)));
        });
    }

    #endregion

    #region Tools

    private static (int Packages, int Answers, int Unanswered, int Once, int Twice, int Thrice, int More) Round(JsonElement rounds, string name)
    {
        var round = rounds.GetProperty(name);

        return (round.GetProperty("packages").GetInt32(), round.GetProperty("answers").GetInt32(), round.GetProperty("unanswered").GetInt32(),
            round.GetProperty("answered_once").GetInt32(), round.GetProperty("answered_twice").GetInt32(), round.GetProperty("answered_thrice").GetInt32(), round.GetProperty("answered_more").GetInt32());
    }

    #endregion
}
