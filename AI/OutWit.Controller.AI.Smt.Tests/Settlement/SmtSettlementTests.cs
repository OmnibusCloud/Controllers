using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The host's rules of a run on rows made up as a node writes them, without nodes and without the
/// solver: copies are one answer exactly when their digests are one; a model the evaluator calls
/// failed goes to three more machines and accuses nobody by itself; machines that agree on such a
/// model settle it by agreement, and no lie is counted; a package a node could not work on, and a
/// row of a fault of the node, go out once more; a model past the cap is cut for the lab and kept
/// whole in the ledger; a row that is not one a node may give fails its package. Every document is
/// held to its schema.
/// </summary>
[TestFixture]
public sealed class SmtSettlementTests
{
    #region Constants

    private const string X_ABOVE_ONE = "(declare-const x Int)(assert (> x 1))";
    private const string X_TWO = "((define-fun x () Int 2))";
    private const string X_THREE = "((define-fun x () Int 3))";
    private const string X_ZERO = "((define-fun x () Int 0))";

    #endregion

    #region Fields

    private SmtSettleRig m_rig = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_rig = new SmtSettleRig();
    }

    [TearDown]
    public void TearDown()
    {
        m_rig.Dispose();
    }

    #endregion

    #region Copies Tests

    [Test]
    public void CopiesWithOneDigestAreOneAnswerTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: [], rate: 1);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(disputes, Is.Empty);
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("sat"));
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(row.GetProperty("model").GetString(), Is.EqualTo(X_TWO));
            Assert.That(Count(run.Integrity, "agreements"), Is.EqualTo(1));
            Assert.That(Count(run.Integrity, "lies"), Is.Zero);
        });
    }

    [Test]
    public void CopiesThatGiveOneVerdictAndTwoDigestsAreADisputeTest()
    {
        // Both models satisfy the problem and both rows say sat: the copies are still not one
        // answer, since they are compared by their digests and by nothing else.
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "the problem goes to three more machines");

        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_THREE)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 1);
        var row = Row(run, 0);
        var dispute = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(row.GetProperty("model").GetString(), Is.EqualTo(X_TWO), "the digest the most of the three gave");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(5));
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.False);
            Assert.That(dispute.GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.DIGESTS_DIFFER));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.RESOLVED));
            Assert.That(Count(run.Integrity, "disagreements"), Is.EqualTo(1));
            Assert.That(Count(run.Integrity, "lies"), Is.EqualTo(2), "the copy of the first round and the copy of the dispute that differ from the settled answer");
            Assert.That(Count(run.Integrity, "problems_with_lies"), Is.EqualTo(1));
        });
    }

    [Test]
    public void ADisputeWithoutAMajorityLeavesTheProblemDisputedTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Unsat()) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Unsat()) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 1);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED));
            Assert.That(row.GetProperty("code").GetString(), Is.EqualTo(SmtRunWords.NO_MAJORITY));
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("none"), "no answer is given");
            Assert.That(row.GetProperty("settled_by").GetString(), Is.Empty);
            Assert.That(Count(run.Integrity, "disputed"), Is.EqualTo(1));
            Assert.That(Count(run.Integrity, "lies"), Is.Zero, "nothing was settled, so nothing differs from a settled answer");
        });
    }

    #endregion

    #region Model Tests

    [Test]
    public void AModelTheEvaluatorCallsFailedGoesToThreeMoreMachinesTest()
    {
        // One copy, not sampled: a wrong model would stand on one machine's word if the evaluator
        // were not asked.
        var (set, options, package) = One(X_ABOVE_ONE, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ZERO)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "a failed model is never taken on one copy");

        var unsettled = Settle(set, options, [package], returned, disputes: disputes, settled: [], rate: 0);
        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 0);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Row(unsettled, 0).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED), "without the three more copies the failed model is no answer");
            Assert.That(Row(unsettled, 0).GetProperty("code").GetString(), Is.EqualTo(SmtRunWords.TOO_FEW_COPIES));

            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(row.GetProperty("model").GetString(), Is.EqualTo(X_TWO));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(Integrity(run).GetProperty("disputes")[0].GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.MODEL_FAILED));
            Assert.That(Count(run.Integrity, "lies"), Is.EqualTo(1), "the copy that differs from the settled answer of the others");
            Assert.That(Count(run.Integrity, "failed_models"), Is.EqualTo(1));
            Assert.That(Count(run.Integrity, "evaluator_solver_disagreements"), Is.Zero);
        });
    }

    [Test]
    public void MachinesThatAgreeOnAFailedModelAccuseNobodyTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ZERO)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "a failed model goes to three more machines even when its copies agree");

        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 1);
        var row = Row(run, 0);
        var loud = Integrity(run).GetProperty("evaluator_solver_disagreements");

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.AGREEMENT));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.FAILED), "the row says what the evaluator said");
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(5));
            Assert.That(Count(run.Integrity, "lies"), Is.Zero, "no machine is counted as lying");
            Assert.That(Count(run.Integrity, "problems_with_lies"), Is.Zero);
            Assert.That(Count(run.Integrity, "evaluator_solver_disagreements"), Is.EqualTo(1), "a disagreement between the evaluator and the solver");
            Assert.That(loud.GetArrayLength(), Is.EqualTo(1));
            Assert.That(loud[0].GetProperty("index").GetInt32(), Is.Zero, "named by its index, loudly");
            Assert.That(loud[0].GetProperty("reason").GetString(), Is.EqualTo("assertion_false"));
            Assert.That(Report(run).GetProperty("copies").GetProperty("settled_by").GetProperty(SmtRunWords.AGREEMENT).GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void AMajorityOnAFailedModelMakesNoLiarOfTheMachineThatDiffersTest()
    {
        // One copy, a failed model A: three more machines. Two give A again, the third a model B the
        // evaluator validates. The machines' answer is A, which the server itself finds wrong: the
        // third machine is no liar for differing from it, and nobody else is either.
        var (set, options, package) = One(X_ABOVE_ONE, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ZERO)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 0);

        AssertAgreementWithoutLiars(run, copies: 4);
    }

    [Test]
    public void AMajorityOnAFailedModelMakesNoLiarOfAnyCopyOfTheFirstRoundTest()
    {
        // The same with the first round in it: its copies give B and A, which differ; three more
        // give A, A and B. A leads, the evaluator rejects it, and no copy is counted as a lie, the
        // copies of B in either round included.
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ZERO)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_ZERO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)) };
        var run = Settle(set, options, [package], returned, disputes: disputes, settled: settled, rate: 1);

        AssertAgreementWithoutLiars(run, copies: 5);
        Assert.That(Integrity(run).GetProperty("disputes")[0].GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.DIGESTS_DIFFER));
    }

    [Test]
    public void AModelPastTheCapIsCutForTheLabAndKeptWholeInTheLedgerTest()
    {
        const int CONSTANTS = 3_000;
        var problem = new StringBuilder();
        var model = new StringBuilder("(");
        for (var index = 0; index < CONSTANTS; index++)
        {
            problem.Append($"(declare-const c{index} Int)(assert (> c{index} 0))");
            model.Append($"(define-fun c{index} () Int 1)");
        }

        model.Append(')');
        var (set, options, package) = One(problem.ToString(), rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(model.ToString())) };

        var run = Settle(set, options, [package], returned, disputes: [], settled: [], rate: 0);
        var row = Row(run, 0);
        var full = JsonDocument.Parse(run.Models.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(model.Length, Is.GreaterThan(SmtRunRig.MaxModelBytes));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "the whole model was checked");
            Assert.That(row.GetProperty("model").ValueKind, Is.EqualTo(JsonValueKind.Null), "the lab's row does not carry it");
            Assert.That(row.GetProperty("model_truncated").GetBoolean(), Is.True);
            Assert.That(Encoding.UTF8.GetByteCount(run.Results), Is.LessThan(SmtRunRig.MaxModelBytes), "the row is far smaller than the model");
            Assert.That(full.GetProperty("index").GetInt32(), Is.Zero);
            Assert.That(full.GetProperty("models")[0].GetString(), Is.EqualTo(model.ToString()), "the full model stays in the ledger");
        });
    }

    #endregion

    #region Repair Tests

    [Test]
    public void APackageANodeCouldNotWorkOnGoesOutOnceMoreTest()
    {
        var options = new SmtOptionsData { SamplingRate = 0 };
        var set = SmtSets.Of(Enumerable.Range(0, 4).Select(index => $$"""{"id":"p{{index}}","smt2":"(declare-const x Int)(assert (> x {{index}}))"}"""));
        var problems = SmtSettleRig.Read(set, options);
        var first = m_rig.Publish(problems.Take(2).ToList(), options);
        var second = m_rig.Publish(problems.Skip(2).ToList(), options);

        var faulted = m_rig.Answer(first, _ => SmtSettleRig.Unsat());
        var returned = new[] { SmtRunRig.Fault(faulted, SmtNodeFaults.SANDBOX_UNAVAILABLE), m_rig.Answer(second, _ => SmtSettleRig.Unsat()) };

        var repairs = m_rig.Repairs([first, second], returned);
        var repaired = repairs.Select(package => m_rig.Answer(package, _ => SmtSettleRig.Unsat())).ToArray();
        var run = Settle(set, options, [first, second], returned, repairs, repaired, disputes: [], settled: [], rate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(package => package.ProblemCount), Is.EqualTo(2), "the faulted package's problems, and no other");
            Assert.That(run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("verdict").GetString()), Has.All.EqualTo("unsat"));
            Assert.That(Count(run.Integrity, "repairs"), Is.EqualTo(2));
            Assert.That(Integrity(run).GetProperty("summary").GetProperty("repairs_by_reason").GetProperty(SmtNodeFaults.SANDBOX_UNAVAILABLE).GetInt32(), Is.EqualTo(2));
            Assert.That(Integrity(run).GetProperty("summary").GetProperty("node_faults_by_code").GetProperty(SmtNodeFaults.SANDBOX_UNAVAILABLE).GetInt32(), Is.EqualTo(1));
            Assert.That(Count(run.Integrity, "no_result"), Is.Zero);
        });
    }

    [Test]
    public void AProblemOnlyFaultsOfTheNodeAnswerHasNoResultAfterTheRepairRoundTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Backstop()) };

        var repairs = m_rig.Repairs([package], returned);
        var repaired = repairs.Select(again => m_rig.Answer(again, _ => SmtSettleRig.Backstop())).ToArray();
        var run = Settle(set, options, [package], returned, repairs, repaired, disputes: [], settled: [], rate: 0);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.NO_RESULT));
            Assert.That(row.GetProperty("code").GetString(), Is.EqualTo("backstop"), "what the nodes said last");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.Zero);
            Assert.That(Count(run.Integrity, "no_result"), Is.EqualTo(1));
            Assert.That(Integrity(run).GetProperty("summary").GetProperty("no_result_by_reason").GetProperty("backstop").GetInt32(), Is.EqualTo(1));
            Assert.That(Report(run).GetProperty("statuses").GetProperty(SmtRunWords.NO_RESULT).GetInt32(), Is.EqualTo(1));
            Assert.That(Report(run).GetProperty("verdicts").GetProperty("unsat").GetInt32() + Report(run).GetProperty("verdicts").GetProperty("sat").GetInt32(), Is.Zero, "out of the counts");
        });
    }

    #endregion

    #region Intake Tests

    [Test]
    public void ARowWhoseDigestIsNotItsOwnFailsItsPackageTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var broken = m_rig.Answer(package, _ =>
        {
            var row = SmtSettleRig.Sat(X_TWO);
            row.Digest = new string('0', 64);
            return row;
        }, seal: false);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), broken };

        var run = Settle(set, options, [package], returned, disputes: [], settled: [], rate: 1);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Integrity(run).GetProperty("summary").GetProperty("rejected").GetProperty("content").GetInt32(), Is.EqualTo(1));
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(1), "the honest copy stands alone");
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
        });
    }

    [Test]
    public void AResultUnderAForeignTicketIsRefusedTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var foreign = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));
        foreign.Ticket = SmtNodeRig.NewTicket();
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), foreign };

        var run = Settle(set, options, [package], returned, disputes: [], settled: [], rate: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Integrity(run).GetProperty("summary").GetProperty("rejected").GetProperty("unknown_ticket").GetInt32(), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(1));
            Assert.That(run.Report + run.Integrity + run.Results, Does.Not.Contain(foreign.Ticket));
        });
    }

    [Test]
    public void ARowForAProblemOfAnotherPackageFailsItsPackageTest()
    {
        var (set, options, package) = One(X_ABOVE_ONE, rate: 1);
        var foreign = m_rig.Answer(package, _ =>
        {
            var row = SmtSettleRig.Sat(X_TWO);
            row.Ticket = SmtNodeRig.NewTicket();
            return row;
        });
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), foreign };

        var run = Settle(set, options, [package], returned, disputes: [], settled: [], rate: 1);

        Assert.That(Integrity(run).GetProperty("summary").GetProperty("rejected").GetProperty("content").GetInt32(), Is.EqualTo(1), "a row under another ticket than its package's");
    }

    #endregion

    #region Report Tests

    [Test]
    public void UnsatAnswersOfOneMachineAreListedTest()
    {
        var options = new SmtOptionsData { SamplingRate = 0 };
        var set = SmtSets.Of(["{\"id\":\"a\",\"smt2\":\"(declare-const x Int)(assert (> x x))\"}", "{\"id\":\"b\",\"smt2\":\"(declare-const x Int)(assert (> x 1))\"}"]);
        var package = m_rig.Publish(SmtSettleRig.Read(set, options), options);
        var returned = new[] { m_rig.Answer(package, problem => problem.Id == "a" ? SmtSettleRig.Unsat() : SmtSettleRig.Sat(X_TWO)) };

        var run = Settle(set, options, [package], returned, disputes: [], settled: [], rate: 0);
        var listed = Report(run).GetProperty("unsat_one_copy");

        Assert.Multiple(() =>
        {
            Assert.That(listed.GetProperty("count").GetInt32(), Is.EqualTo(1));
            Assert.That(listed.GetProperty("indices").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(new[] { 0 }));
            Assert.That(Count(run.Integrity, "unsat_one_copy"), Is.EqualTo(1));
        });
    }

    #endregion

    #region Tools

    /// <summary>A set of one problem, read with options of a rate, and its one package.</summary>
    private (string Set, SmtOptionsData Options, SmtTaskPackageData Package) One(string smt2, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of([JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = "p0", ["smt2"] = smt2 })]);
        return (set, options, m_rig.Publish(SmtSettleRig.Read(set, options), options));
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData>? repairs = null, IReadOnlyList<SmtResultPackageData>? repaired = null,
        IReadOnlyList<SmtTaskPackageData>? disputes = null, IReadOnlyList<SmtResultPackageData>? settled = null, double? rate = null)
    {
        var run = m_rig.Settle(set, options, issued, returned, repairs ?? [], repaired ?? [], disputes, settled, rate);
        AssertSchemas(run);
        return run;
    }

    private static void AssertSchemas(SmtSettledDocuments run)
    {
        SmtRunChecks.AssertSchemas(run);
    }

    /// <summary>A problem settled by agreement on the failed model A over copies of B: failed, listed loudly, and no liar.</summary>
    private static void AssertAgreementWithoutLiars(SmtSettledDocuments run, int copies)
    {
        var row = Row(run, 0);
        var loud = Integrity(run).GetProperty("evaluator_solver_disagreements");

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.AGREEMENT));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.FAILED));
            Assert.That(row.GetProperty("model").GetString(), Is.EqualTo(X_ZERO), "the machines' answer");
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.False, "the copies did not all give one digest");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(copies));
            Assert.That(Count(run.Integrity, "lies"), Is.Zero, "no copy is a lie against an answer the evaluator rejects");
            Assert.That(Count(run.Integrity, "problems_with_lies"), Is.Zero);
            Assert.That(Count(run.Integrity, "evaluator_solver_disagreements"), Is.EqualTo(1));
            Assert.That(loud.GetArrayLength(), Is.EqualTo(1));
            Assert.That(loud[0].GetProperty("index").GetInt32(), Is.Zero, "named by its index, loudly");
            Assert.That(Integrity(run).GetProperty("disputes")[0].GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.RESOLVED), "the dispute record stands");
        });
    }

    private static JsonElement Row(SmtSettledDocuments run, int index)
    {
        return run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).Single(row => row.GetProperty("index").GetInt32() == index);
    }

    private static JsonElement Integrity(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Integrity).RootElement;
    }

    private static JsonElement Report(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Report).RootElement;
    }

    private static int Count(string integrity, string name)
    {
        return JsonDocument.Parse(integrity).RootElement.GetProperty("summary").GetProperty(name).GetInt32();
    }

    #endregion
}
