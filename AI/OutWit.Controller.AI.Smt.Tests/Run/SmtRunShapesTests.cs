using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// The host activities in the other shapes a script may call them in, through the engine and their
/// adapters: <c>Smt.Settle</c> without a rate, with and without a dispute round, <c>Smt.Repairs</c>
/// without a rate, <c>Smt.Disputes</c> without a repair round; and SmtRun on a set whose every row
/// is refused. A node lies about the cost of its rows, so there is something to dispute.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunShapesTests : SmtRunTestBase
{
    #region Constants

    private const int PROBLEMS = 32;

    // Smt.Repairs and Smt.Disputes without the rate and the repair round, and Smt.Settle without
    // the rate and the dispute round: the activities' shortest calls.
    private const string WITHOUT_DISPUTES = """
        Job:SmtShortest(Blob:problems, SmtOptions:opts)
        {
            SmtTaskPackageCollection:packages = Smt.Split(problems, opts);
            ProcessingOptions:twice = Grid.Replicas(2);
            SmtResultPackageCollection:solved = Grid.ForEach(package in packages, twice)
                => Smt.SolvePackage(package);
            SmtTaskPackageCollection:repairs = Smt.Repairs(packages, solved);
            SmtResultPackageCollection:repaired = Grid.ForEach(package in repairs, twice)
                => Smt.SolvePackage(package);
            SmtTaskPackageCollection:disputes = Smt.Disputes(packages, solved);
            Blob:ledger = Smt.Settle(problems, opts, packages, solved, repairs, repaired);
            Blob:results = Smt.CollectResults(ledger);
            Blob:report = Smt.Collect(ledger);
            Blob:provenance = Smt.Provenance(ledger);
            Blob:integrity = Smt.Integrity(ledger);
        }
        """;

    // Smt.Settle with a dispute round and without the rate.
    private const string WITHOUT_RATE = """
        Job:SmtWithoutRate(Blob:problems, SmtOptions:opts)
        {
            SmtTaskPackageCollection:packages = Smt.Split(problems, opts);
            ProcessingOptions:twice = Grid.Replicas(2);
            SmtResultPackageCollection:solved = Grid.ForEach(package in packages, twice)
                => Smt.SolvePackage(package);
            SmtTaskPackageCollection:repairs = Smt.Repairs(packages, solved);
            SmtResultPackageCollection:repaired = Grid.ForEach(package in repairs, twice)
                => Smt.SolvePackage(package);
            SmtTaskPackageCollection:disputes = Smt.Disputes(packages, solved, repairs, repaired);
            ProcessingOptions:thrice = Grid.Replicas(3);
            SmtResultPackageCollection:settled = Grid.ForEach(package in disputes, thrice)
                => Smt.SolvePackage(package);
            Blob:ledger = Smt.Settle(problems, opts, packages, solved, repairs, repaired, disputes, settled);
            Blob:results = Smt.CollectResults(ledger);
            Blob:report = Smt.Collect(ledger);
            Blob:provenance = Smt.Provenance(ledger);
            Blob:integrity = Smt.Integrity(ledger);
        }
        """;

    #endregion

    #region Fields

    private string m_set = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_set = SmtSets.Of(Enumerable.Range(0, PROBLEMS).Select(Line));
    }

    [SetUp]
    public void Reset()
    {
        // The first node says every row of its cost one unit more than it was: its copies differ
        // from the honest ones by their digests.
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows)
            {
                row.ResourceCount++;
                row.Digest = SmtResultDigest.Of(row);
            }
        }));
    }

    #endregion

    #region Shape Tests

    [Test]
    public async Task SettleWithoutARateAndWithoutADisputeRoundTest()
    {
        var run = await RunScriptAsync(WITHOUT_DISPUTES, new SmtOptionsData { SamplingRate = 1 });
        var disputes = Packages(run, "disputes");
        var disputed = run.Rows.Where(row => row.GetProperty("status").GetString() == SmtRunWords.DISPUTED).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(disputes.Sum(package => package.ProblemCount), Is.EqualTo(disputed.Count), "Smt.Disputes without the repair round sent every problem the liar answered");
            Assert.That(disputed, Is.Not.Empty);
            Assert.That(disputed.Select(row => row.GetProperty("code").GetString()), Has.All.EqualTo(SmtRunWords.TOO_FEW_COPIES), "no dispute round answered them");
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo("not_stated"));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling_rate").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.FanOut("main").Selected, Is.EqualTo(-1), "the main fan-out's selected is null without a rate");
            Assert.That(run.Integrity.GetProperty("fan_outs").EnumerateArray().Select(fanOut => fanOut.GetProperty("round").GetString()), Is.EqualTo(new[] { "main", "repair" }), "and there is no dispute round");
            Assert.That(run.IntegrityCount("repairs"), Is.Zero, "Smt.Repairs without the rate had nothing to send");
            Assert.That(run.Rows.Where(row => row.GetProperty("status").GetString() != SmtRunWords.DISPUTED).Select(row => row.GetProperty("settled_by").GetString()),
                Has.All.EqualTo(SmtRunWords.COPIES), "what the honest machines answered alone stands on two");
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task SettleWithADisputeRoundAndWithoutARateTest()
    {
        var run = await RunScriptAsync(WITHOUT_RATE, new SmtOptionsData { SamplingRate = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("disputes"), Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("disputes_resolved"), Is.EqualTo(run.IntegrityCount("disputes")), "two honest machines of three settle every dispute");
            Assert.That(run.IntegrityCount("lies"), Is.GreaterThan(0));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo("not_stated"));
            Assert.That(run.FanOut("main").Selected, Is.EqualTo(-1));
            Assert.That(run.FanOut("dispute").Copies, Is.EqualTo(3));
            Assert.That(run.Rows.Select(row => row.GetProperty("status").GetString()), Has.None.EqualTo(SmtRunWords.DISPUTED));
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ASetWhoseEveryRowIsRefusedGivesItsDocumentsWithoutAPackageTest()
    {
        Liars.Lie = null;
        var set = SmtSets.Of(Enumerable.Range(0, 5).Select(index => SmtSets.Refused($"refused-{index}")));

        var run = await RunAsync(set, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(run.Rows, Has.Count.EqualTo(5));
            Assert.That(run.Rows.Select(row => row.GetProperty("status").GetString()), Has.All.EqualTo(SmtRunWords.REFUSED));
            Assert.That(run.Rows.Select(row => row.GetProperty("code").GetString()), Has.All.EqualTo("field_missing"), "each says why");
            Assert.That(Packages(run, "packages"), Is.Empty, "nothing is sent");
            Assert.That(run.FanOut("main").Packages, Is.Zero);
            Assert.That(run.Report.GetProperty("problem_set").GetProperty("refused").GetInt32(), Is.EqualTo(5));
            Assert.That(run.IntegrityCount("problems"), Is.Zero);
            Assert.That(run.Integrity.GetProperty("summary").GetProperty("binding").GetString(), Is.EqualTo(SmtRunWords.NO_RESULTS), "no result came back");
        });

        SmtRunChecks.AssertSchemas(run);
    }

    #endregion

    #region Tools

    /// <summary>A satisfiable check and an unsatisfiable one, by turns.</summary>
    private static string Line(int index)
    {
        const string LIA = "(set-logic QF_LIA)(declare-const x Int)";

        var smt2 = index % 2 == 0
            ? $"{LIA}(assert (> x {index}))(assert (< x {index + 5}))"
            : $"{LIA}(assert (> x {index}))(assert (< x {index}))";

        return $"{{\"id\":\"s{index}\",\"query\":\"check\",\"smt2\":\"{smt2}\"}}";
    }

    /// <summary>Runs a script of the test's own on the fixture's set and reads its documents.</summary>
    private async Task<SmtRunDocuments> RunScriptAsync(string script, SmtOptionsData options)
    {
        var job = Engine.Compile(script);
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(m_set, "problems.jsonl"), options);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        return await SmtRunDocuments.ReadAsync(job, Blobs);
    }

    /// <summary>The packages of problems a variable of the job holds.</summary>
    private static List<SmtTaskPackageData> Packages(SmtRunDocuments run, string name)
    {
        return run.Job.Variables[name].Value is IEnumerable<SmtTaskPackageData?> packages ? packages.OfType<SmtTaskPackageData>().ToList() : [];
    }

    #endregion
}
