using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtRun through the engine with the Grid of this repository: Grid tells the return collection of
/// every fan-out which package each result was returned for, and the host holds a result to it.
/// The results Grid told are the very objects <c>Smt.Repairs</c>, <c>Smt.Disputes</c> and
/// <c>Smt.Settle</c> read from the job's variables; a node that answers one of its packages twice,
/// in place of another, is refused for the package it robbed, its answer is one machine's word,
/// and the robbed package is answered by the repair round.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunBindingTests : SmtRunTestBase
{
    #region Constants

    // Six packages of sixteen: two of them on each of the rig's three nodes, so a node has a
    // package to answer twice and one to rob.
    private const int PROBLEMS = 96;

    private static readonly string[] FAN_OUTS = ["solved", "repaired", "settled"];

    #endregion

    #region Fields

    private string m_set = null!;
    private Task<SmtRunDocuments>? m_honest;

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
        Liars.Lie = null;
        Liars.Twice = null;
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
    }

    #endregion

    #region Binding Tests

    [Test]
    public async Task EveryResultTheActivitiesReadIsBoundToThePackageItNamesTest()
    {
        // The variables the activities of the job read their rounds from, after the job: every
        // result of every fan-out is the object Grid told, bound to the package it names. SmtRun
        // empties those variables once its documents are written, so the run here leaves out its
        // last statements and keeps them as the activities read them.
        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 }, ScriptKeepingResults());
        var results = FAN_OUTS.SelectMany(name => Results(run, name)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Results(run, "solved"), Has.Count.EqualTo(2 * run.FanOut("main").Packages), "every package of the first fan-out came back twice");
            Assert.That(results.Select(SmtRunRig.BoundTicket), Is.EqualTo(results.Select(result => result.Ticket)), "every result is bound to the package it names");
        });
    }

    #endregion

    #region Twice Tests

    [Test]
    public async Task ANodeThatAnswersOneOfItsPackagesTwiceInPlaceOfAnotherIsRefusedTest()
    {
        // Rate 0: no package is asked for twice. The liar answers the first package of its share
        // honestly, and once more, as a blob of its own, for the task of its second package. Grid
        // tells the host which task the second answer came back for, and the host refuses it.
        Liars.Twice = doubled => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, doubled, _ => { }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 0 });
        var (doubledProblems, robbedProblems) = Problems(run);
        var copies = run.Rows.Where(row => row.GetProperty("settled_by").GetString() == SmtRunWords.COPIES).Select(row => row.GetProperty("index").GetInt32()).ToList();
        var unsat = doubledProblems.Where(index => run.Row(index).GetProperty("verdict").GetString() == SmtResultWords.UNSAT).ToList();

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0), "the liar had two packages to lie with");
            Assert.That(run.IntegrityCount("rejected", "other_package"), Is.EqualTo(Liars.Lies), "every second answer is refused");
            Assert.That(run.IntegrityCount("rejected_results"), Is.EqualTo(Liars.Lies), "and nothing else is");
            Assert.That(doubledProblems.Select(index => run.Row(index).GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.ONE_COPY), "the doubled package rests on one machine");
            Assert.That(doubledProblems.Select(index => run.Row(index).GetProperty("copies").GetInt32()), Has.All.EqualTo(1));
            Assert.That(unsat, Is.Not.Empty, "the doubled package has unsat answers");
            Assert.That(run.Report.GetProperty("unsat_one_copy").GetProperty("indices").EnumerateArray().Select(index => index.GetInt32()), Is.SupersetOf(unsat), "listed as one machine's");
            Assert.That(copies, Is.EquivalentTo(robbedProblems), "no problem is settled by copies on one machine's word: only the robbed package's, by the repair pair");
            Assert.That(run.IntegrityCount("repairs"), Is.EqualTo(robbedProblems.Count), "the robbed package goes out again");
            Assert.That(run.IntegrityCount("repairs_by_reason", "other_package"), Is.EqualTo(robbedProblems.Count), "under what became of its task");
            Assert.That(run.IntegrityCount("no_result"), Is.Zero, "and is answered");
            Assert.That(run.Integrity.GetProperty("summary").GetProperty("binding").GetString(), Is.EqualTo(SmtRunWords.BOUND));
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task SmtRepairsReadsTheResultsGridToldTest()
    {
        // The second answer is broken as well. Read unbound, it would be a refused result under
        // the doubled package's ticket: two results came back for that package, so it would be
        // taken as asked for twice, and its problems, with one answer each, would go out again as
        // copies_short. Smt.Repairs reads the results Grid told: the broken one came back for the
        // robbed package, which goes out again, and the doubled package does not.
        Liars.Twice = doubled => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, doubled, rows => rows[0].Digest = new string('0', 64)));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 0 });
        var (_, robbedProblems) = Problems(run);

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("rejected", "other_package"), Is.EqualTo(Liars.Lies));
            Assert.That(run.IntegrityCount("rejected", "content"), Is.Zero, "the broken answer is refused before its blob is read");
            Assert.That(run.IntegrityCount("repairs_by_reason", SmtRunRig.CopiesShort), Is.Zero, "the doubled package was not taken as asked for twice");
            Assert.That(run.IntegrityCount("repairs"), Is.EqualTo(robbedProblems.Count), "only the robbed package went out again");
        });
    }

    [Test]
    public async Task SmtDisputesReadsTheResultsGridToldTest()
    {
        // The second answer differs from the first by its digest. Read unbound, the doubled
        // package's problems would have two answers that disagree and go to three more machines.
        // Smt.Disputes reads the results Grid told: the second answer is no answer, and nothing
        // is disputed.
        Liars.Twice = doubled => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, doubled, rows =>
        {
            foreach (var row in rows)
            {
                row.ResourceCount++;
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 0 });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("rejected", "other_package"), Is.EqualTo(Liars.Lies));
            Assert.That(run.FanOut("dispute").Packages, Is.Zero, "Smt.Disputes sent nothing to three more machines");
            Assert.That(run.IntegrityCount("disputes"), Is.Zero);
            Assert.That(run.IntegrityCount("lies"), Is.Zero, "a refused answer is no copy that differs");
        });
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

        return $"{{\"id\":\"b{index}\",\"query\":\"check\",\"smt2\":\"{smt2}\"}}";
    }

    /// <summary>The problems of the packages of the first fan-out the liar answered twice, and of those it robbed.</summary>
    private (List<int> Doubled, List<int> Robbed) Problems(SmtRunDocuments run)
    {
        var packages = ((IEnumerable<SmtTaskPackageData?>)run.Job.Variables["packages"].Value!).OfType<SmtTaskPackageData>().ToDictionary(package => package.Ticket);
        var doubled = Liars.Doubled.Where(pair => packages.ContainsKey(pair.Doubled) && packages.ContainsKey(pair.Robbed)).ToList();

        return (doubled.SelectMany(pair => SmtRunRig.Indices(Blobs, packages[pair.Doubled])).Order().ToList(),
            doubled.SelectMany(pair => SmtRunRig.Indices(Blobs, packages[pair.Robbed])).Order().ToList());
    }

    /// <summary>Every row of a run is the honest run's at the same rate: the same status, verdict and digest.</summary>
    private async Task AssertHonestAsync(SmtRunDocuments run)
    {
        var twice = Liars.Twice;
        var lies = Liars.Lies;
        Liars.Twice = null;
        SmtRunDocuments honest;
        try
        {
            honest = await (m_honest ??= RunAsync(m_set, new SmtOptionsData { SamplingRate = 0 }));
        }
        finally
        {
            Liars.Twice = twice;
        }

        Assert.That(Liars.Lies, Is.EqualTo(lies), "the honest run was made before the lie, or by nodes that did not lie");
        Assert.Multiple(() =>
        {
            Assert.That(run.Rows, Has.Count.EqualTo(PROBLEMS));
            foreach (var row in honest.Rows)
            {
                var index = row.GetProperty("index").GetInt32();
                var settled = run.Row(index);
                Assert.That(settled.GetProperty("status").GetString(), Is.EqualTo(row.GetProperty("status").GetString()), $"problem {index}");
                Assert.That(settled.GetProperty("verdict").GetString(), Is.EqualTo(row.GetProperty("verdict").GetString()), $"problem {index}");
                Assert.That(settled.GetProperty("digest").GetString(), Is.EqualTo(row.GetProperty("digest").GetString()), $"problem {index}: the honest row");
            }
        });
    }

    /// <summary>The result packages a fan-out of the job returned, as the job's variables hold them: the objects themselves.</summary>
    private static List<SmtResultPackageData> Results(SmtRunDocuments run, string name)
    {
        return run.Job.Variables[name].Value is IEnumerable<SmtResultPackageData?> results ? results.OfType<SmtResultPackageData>().ToList() : [];
    }

    #endregion
}
