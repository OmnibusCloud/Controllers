using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtRun against nodes that lie, end to end through the engine: three in-process nodes really
/// solve the set, and a liar returns something else in place of every result package it made.
/// Each test is one way of lying. In every one the job completes, every settled row is the honest
/// answer, the integrity document counts what was done, and no document names a machine. The last
/// test has every machine give the same model the evaluator calls failed: the machines agree, the
/// row says failed and settled by agreement, and no machine is counted as lying.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunLyingNodesTests : SmtRunTestBase
{
    #region Constants

    private const int PROBLEMS = 40;
    private const string WRONG_MODEL = "(\n  (define-fun x () Int\n    (- 1000))\n)";
    private const string WRONG_VALUES = "((x (- 1000)))";

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
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
    }

    #endregion

    #region Lying Tests

    [Test]
    public async Task AnHonestRunHasNothingToReportTest()
    {
        var run = await HonestAsync();

        Assert.Multiple(() =>
        {
            Assert.That(run.Rows, Has.Count.EqualTo(PROBLEMS));
            Assert.That(run.Rows.Select(row => row.GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.COPIES));
            Assert.That(run.IntegrityCount("lies") + run.IntegrityCount("disputes") + run.IntegrityCount("rejected_results") + run.IntegrityCount("node_faults"), Is.Zero);
            Assert.That(run.Rows.Count(row => row.GetProperty("verdict").GetString() == SmtResultWords.SAT), Is.GreaterThan(PROBLEMS / 3), "the set has models to lie about");
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [TestCase(1.0)]
    [TestCase(0.0)]
    public async Task ANodeThatAnswersSatWithAWrongModelIsFoundOutTest(double rate)
    {
        // Every sat row of the liar carries a model the problem rules out, its digest made anew so
        // that the row holds. Copied, it disagrees with the honest copy; alone, the evaluator calls
        // its model failed. Either way three more machines settle it.
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.SAT && row.Models.Count > 0))
            {
                row.Models = row.Models.Select(_ => WRONG_MODEL).ToList();
                row.Values = row.Values.Select(_ => WRONG_VALUES).ToList();
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = rate });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0), "the liar answered");
            Assert.That(run.IntegrityCount("lies"), Is.GreaterThan(0), "the integrity document counts the lies");
            Assert.That(run.IntegrityCount("failed_models"), Is.GreaterThan(0), "and the models the evaluator called failed");
            Assert.That(run.IntegrityCount("disputes_resolved"), Is.EqualTo(run.IntegrityCount("disputes")), "two honest machines of three settle every dispute");
            Assert.That(run.IntegrityCount("evaluator_solver_disagreements"), Is.Zero, "honest machines and the evaluator agree");
            Assert.That(run.Rows.Where(row => row.GetProperty("settled_by").GetString() == SmtRunWords.DISPUTE).Select(row => row.GetProperty("validation").GetString()),
                Has.All.Not.EqualTo(SmtRunWords.FAILED));
        });

        if (rate == 0)
            Assert.That(run.IntegrityCount("dispute_causes", SmtRunWords.MODEL_FAILED), Is.GreaterThan(0), "alone, the failed model sent the problem on");

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ANodeThatAnswersUnsatToASatisfiableProblemIsOutvotedTest()
    {
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.SAT))
            {
                row.Verdict = SmtResultWords.UNSAT;
                row.Models = [];
                row.Values = [];
                row.Count = 0;
                row.Complete = row.Query == SmtQuery.Enumerate;
                row.LimitReached = false;
                row.Proof = row.Query == SmtQuery.Prove ? SmtResultWords.PROVED : SmtResultWords.NONE;
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("disagreements"), Is.GreaterThan(0), "the copies' digests differ");
            Assert.That(run.IntegrityCount("lies"), Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("disputes_resolved"), Is.EqualTo(run.IntegrityCount("disputes")));
        });

        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ANodeThatChangesAValueAndKeepsTheVerdictIsOutvotedTest()
    {
        // The value of x moved by one, which may still satisfy the problem: nothing but the
        // comparison of digests tells the copies apart, and that is enough.
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.SAT && row.Models.Count > 0))
            {
                row.Models = row.Models.Select(Shift).ToList();
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("lies"), Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("dispute_causes", SmtRunWords.DIGESTS_DIFFER), Is.GreaterThan(0));
        });

        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ANodeThatReturnsAFaultCodeForEveryPackageCostsNothingButItsShareTest()
    {
        // Every package was asked for twice; the liar's copy of each it held came back as a fault
        // code, so the honest copy stood alone. Those problems go out again, to two machines: a
        // pair that missed the liar restores the copies, a pair that met it again leaves the
        // honest answers of two fan-outs, which may be one machine's, and the sampling short.
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Fault(honest, SmtNodeFaults.SANDBOX_UNAVAILABLE));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });
        var alone = run.Rows.Count(row => row.GetProperty("settled_by").GetString() == SmtRunWords.ONE_COPY);

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("node_faults"), Is.EqualTo(Liars.Lies), "every package it gave back so is counted");
            Assert.That(run.IntegrityCount("node_faults_by_code", SmtNodeFaults.SANDBOX_UNAVAILABLE), Is.EqualTo(Liars.Lies));
            Assert.That(run.IntegrityCount("rejected_results"), Is.Zero, "a node's own fault code is not a refused result");
            Assert.That(run.IntegrityCount("repairs_by_reason", SmtRunRig.CopiesShort), Is.GreaterThan(0), "the problems the liar left with one answer went out again");
            Assert.That(run.IntegrityCount("repairs"), Is.EqualTo(run.IntegrityCount("repairs_by_reason", SmtRunRig.CopiesShort)), "and nothing else did");
            Assert.That(run.Rows.Select(row => row.GetProperty("settled_by").GetString()), Has.All.AnyOf(SmtRunWords.COPIES, SmtRunWords.ONE_COPY));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo(alone > 0 ? "short" : "as_asked"),
                "the sampling is short exactly when a problem rests on one machine");
            Assert.That(run.IntegrityCount("no_result"), Is.Zero);
        });

        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ANodeThatReturnsRowsWithABrokenDigestIsRefusedTest()
    {
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows => rows[0].Digest = new string('0', 64)));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("rejected", "content"), Is.EqualTo(Liars.Lies), "every package with a row whose digest is not its own");
            Assert.That(run.IntegrityCount("disputes"), Is.Zero, "a refused package is no copy to disagree with");
        });

        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task ANodeThatAnswersUnderAForeignTicketIsRefusedTest()
    {
        Liars.Lie = honest =>
        {
            var forged = honest.Clone();
            forged.Ticket = SmtNodeRig.NewTicket();
            return Task.FromResult<SmtResultPackageData?>(forged);
        };

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });

        await AssertHonestAsync(run);
        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("rejected", "unknown_ticket"), Is.EqualTo(Liars.Lies));
        });

        SmtRunChecks.AssertNamesNoMachine(run);
    }

    [Test]
    public async Task MachinesThatAllGiveAModelTheEvaluatorCallsFailedAccuseNobodyTest()
    {
        // Every machine gives the same wrong model for every sat check: the machines agree with one
        // another and the evaluator does not. The row says so, the integrity document counts it as a
        // disagreement between the evaluator and the solver, loudly, and no machine as lying.
        Liars.Liars = SmtLiars.All;
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.SAT && row.Query == SmtQuery.Check))
            {
                row.Models = [WRONG_MODEL];
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });
        var agreed = run.Rows.Where(row => row.GetProperty("settled_by").GetString() == SmtRunWords.AGREEMENT).ToList();
        var loud = run.Integrity.GetProperty("evaluator_solver_disagreements").EnumerateArray().Select(item => item.GetProperty("index").GetInt32()).ToList();
        var checks = Enumerable.Range(0, PROBLEMS).Count(index => index % 4 == 0);

        Assert.Multiple(() =>
        {
            Assert.That(agreed, Has.Count.EqualTo(checks), "every sat check");
            Assert.That(agreed.Select(row => row.GetProperty("validation").GetString()), Has.All.EqualTo(SmtRunWords.FAILED));
            Assert.That(agreed.Select(row => row.GetProperty("agreed").GetBoolean()), Has.All.True);
            Assert.That(agreed.Select(row => row.GetProperty("model").GetString()), Has.All.EqualTo(WRONG_MODEL), "the machines' answer");
            Assert.That(run.IntegrityCount("evaluator_solver_disagreements"), Is.EqualTo(checks));
            Assert.That(loud, Is.EquivalentTo(agreed.Select(row => row.GetProperty("index").GetInt32())), "each by its index");
            Assert.That(run.IntegrityCount("lies"), Is.Zero, "no machine is counted as lying");
            Assert.That(run.IntegrityCount("problems_with_lies"), Is.Zero);
            Assert.That(run.IntegrityCount("dispute_causes", SmtRunWords.MODEL_FAILED), Is.EqualTo(checks), "the failed model sent each to three more machines");
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    #endregion

    #region Tools

    /// <summary>
    /// A problem of the set: a satisfiable check, an unsatisfiable one, an enumeration of two
    /// models, and a proof that holds or one with a counterexample, by turns.
    /// </summary>
    private static string Line(int index)
    {
        const string LIA = "(set-logic QF_LIA)(declare-const x Int)";

        return (index % 4) switch
        {
            0 => Row(index, "check", $"{LIA}(assert (> x {index}))(assert (< x {index + 5}))", ""),
            1 => Row(index, "check", $"{LIA}(assert (> x {index}))(assert (< x {index}))", ""),
            2 => Row(index, "enumerate", $"{LIA}(assert (and (> x {index}) (< x {index + 3})))", ",\"project\":[\"x\"],\"max_models\":5"),
            _ => Row(index, "prove", $"{LIA}(assert (> x {index}))", index / 4 % 2 == 0 ? ",\"goal\":\"(> x 0)\"" : $",\"goal\":\"(> x {index + 1})\"")
        };
    }

    private static string Row(int index, string query, string smt2, string extra)
    {
        return $"{{\"id\":\"q{index}\",\"query\":\"{query}\",\"smt2\":\"{smt2}\"{extra}}}";
    }

    /// <summary>A model with the first number in it moved by one.</summary>
    private static string Shift(string model)
    {
        for (var at = 0; at < model.Length; at++)
        {
            if (!char.IsAsciiDigit(model[at]))
                continue;

            var end = at;
            while (end < model.Length && char.IsAsciiDigit(model[end]))
                end++;

            return model[..at] + (long.Parse(model[at..end]) + 1) + model[end..];
        }

        return model + " ";
    }

    private Task<SmtRunDocuments> HonestAsync()
    {
        return m_honest ??= RunHonestAsync();
    }

    private async Task<SmtRunDocuments> RunHonestAsync()
    {
        var lie = Liars.Lie;
        Liars.Lie = null;
        try
        {
            return await RunAsync(m_set, new SmtOptionsData { SamplingRate = 1 });
        }
        finally
        {
            Liars.Lie = lie;
        }
    }

    /// <summary>Every row of a run is the honest run's: the same status, verdict and digest.</summary>
    private async Task AssertHonestAsync(SmtRunDocuments run)
    {
        var lie = Liars.Lie;
        var liars = Liars.Liars;
        Liars.Lie = null;
        SmtRunDocuments honest;
        try
        {
            honest = await HonestAsync();
        }
        finally
        {
            Liars.Lie = lie;
            Liars.Liars = liars;
        }

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
                Assert.That(settled.GetProperty("validation").GetString(), Is.Not.EqualTo(SmtRunWords.FAILED), $"problem {index}");
            }
        });
    }

    #endregion
}
