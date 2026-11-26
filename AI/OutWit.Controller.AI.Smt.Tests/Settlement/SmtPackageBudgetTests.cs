using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// What a node's rows may cost the host's evaluator: one check has a budget of its own, and the
/// checks of one result package have one together. A node whose every model is made to use up a
/// check's budget makes the host spend no more on a package than its budget and one model; the
/// rows after that are not checked, and say so (<c>budget_package</c>). A package spends only on
/// its own rows, in their order, so <c>Smt.Disputes</c> and <c>Smt.Settle</c> see one check of
/// each row, and a package that has run out does not leave a row unchecked that another package
/// checked to its end. The rows are made up as a node writes them, without nodes and without the
/// solver.
/// </summary>
[TestFixture]
public sealed class SmtPackageBudgetTests
{
    #region Constants

    private const string NON_NEGATIVE = "(declare-const x Int)(assert (>= x 0))";
    private const string X_ONE = "((define-fun x () Int 1))";
    private const string X_MINUS_ONE = "((define-fun x () Int (- 1)))";

    // A model of the node's own making: its function calls itself twice a level, sixty levels
    // down, and the check of the model uses up the evaluator's budget of one check.
    private const string BURNING = "((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (h!0 (- n 1)) (h!0 (- n 1))))) (define-fun x () Int (h!0 60)))";

    // The models a package of the tests' own budget checks: three of them pass it.
    private const int CHECKED = 3;

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

    #region Budget Tests

    [Test]
    public void RowsPastThePackagesBudgetAreNotCheckedTest()
    {
        // Twelve rows, each with a model that uses up a check's budget: the host's budget of the
        // package ends the checks after the models that pass it, and the rest are not read.
        var (set, options, package) = Many(12, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(BURNING)) };

        var run = m_rig.Settle(set, options, [package], returned, [], [], null, null, 0, Budget());
        var reasons = SmtSettledReader.Rows(run).Select(row => (Class: row.GetProperty("validation").GetString(), Reason: row.GetProperty("validation_reason").GetString())).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(reasons.Take(CHECKED), Is.All.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.BUDGET_STEPS)), "checked to the check's budget");
            Assert.That(reasons.Skip(CHECKED), Is.All.EqualTo((SmtRunWords.PARTIAL, SmtRunWords.BUDGET_PACKAGE)), "not checked: the package's budget is spent");
            Assert.That(SmtSettledReader.Report(run).GetProperty("validation").GetProperty("partial_by_reason").GetProperty(SmtRunWords.BUDGET_PACKAGE).GetInt32(), Is.EqualTo(12 - CHECKED),
                "the report counts them under their reason");
        });
    }

    [Test]
    public void PackageSpendsAtMostItsBudgetAndOneModelTest()
    {
        var (_, options, package) = Many(12, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(BURNING)) };

        var spent = m_rig.Spent([package], returned, Budget()).Single();

        Assert.That(spent, Is.EqualTo(CHECKED * Price(BURNING)), "the package paid for the models it checked and for nothing else");
        Assert.That(spent, Is.LessThanOrEqualTo(Budget() + Price(BURNING)));
    }

    [Test]
    public void HonestPackageIsCheckedToItsEndUnderTheHostsBudgetTest()
    {
        // Two hundred and fifty-six honest rows, the most a package holds: far inside the host's budget.
        var (set, options, package) = Many(256, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ONE)) };

        var run = m_rig.Settle(set, options, [package], returned, [], [], null, null, 0);

        Assert.That(SmtSettledReader.Rows(run).Select(row => row.GetProperty("validation").GetString()), Is.All.EqualTo(SmtRunWords.VALIDATED));
        Assert.That(m_rig.Spent([package], returned, SmtPackageBudget.DEFAULT_STEPS).Single(), Is.LessThan(SmtPackageBudget.DEFAULT_STEPS / 100));
    }

    [Test]
    public void ModelThePackageCouldNotPayForIsNoReasonToDisputeTest()
    {
        // One copy, not sampled. The wrong model of the last row stands past the package's budget:
        // it is not checked, by Smt.Disputes or by Smt.Settle, and neither calls it failed. Both see
        // the same budget, so the problem is not sent to three more machines and is not ruled on as
        // if it had been.
        var (set, options, package) = Many(CHECKED + 2, rate: 0);
        var returned = new[] { m_rig.Answer(package, problem => SmtSettleRig.Sat(problem.Index == CHECKED + 1 ? X_MINUS_ONE : BURNING)) };

        var disputes = m_rig.Disputes([package], returned, [], [], Budget());
        var run = m_rig.Settle(set, options, [package], returned, [], [], disputes, [], 0, Budget());
        var last = SmtSettledReader.Row(run, CHECKED + 1);

        Assert.Multiple(() =>
        {
            Assert.That(disputes, Is.Empty);
            Assert.That(last.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.PARTIAL));
            Assert.That(last.GetProperty("validation_reason").GetString(), Is.EqualTo(SmtRunWords.BUDGET_PACKAGE));
            Assert.That(last.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
        });
    }

    [Test]
    public void ModelThePackagePaysForIsDisputedByBothActivitiesTest()
    {
        // The same wrong model inside the budget: both activities call it failed.
        var (set, options, package) = Many(CHECKED, rate: 0);
        var returned = new[] { m_rig.Answer(package, problem => SmtSettleRig.Sat(problem.Index == CHECKED - 1 ? X_MINUS_ONE : BURNING)) };

        var disputes = m_rig.Disputes([package], returned, [], [], Budget());
        var run = m_rig.Settle(set, options, [package], returned, [], [], disputes, [], 0, Budget());
        var last = SmtSettledReader.Row(run, CHECKED - 1);

        Assert.Multiple(() =>
        {
            Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "the failed model goes to three more machines");
            Assert.That(last.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED));
            Assert.That(SmtSettledReader.Integrity(run).GetProperty("disputes")[0].GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.MODEL_FAILED));
        });
    }

    [Test]
    public void PackageThatRanOutDoesNotLeaveARowUncheckedThatAnotherPackageChecksTest()
    {
        // A package asked for twice. One node spends its package's budget on rows of its own
        // making and gives the last row as the honest node gives it: the honest node's package
        // checks that row to its end, and the row is validated.
        var (set, options, package) = Many(CHECKED + 1, rate: 1);
        var honest = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_ONE));
        var spending = m_rig.Answer(package, problem => SmtSettleRig.Sat(problem.Index == CHECKED ? X_ONE : BURNING));

        var run = m_rig.Settle(set, options, [package], [honest, spending], [], [], null, null, 1, Budget());
        var last = SmtSettledReader.Row(run, CHECKED);

        Assert.Multiple(() =>
        {
            Assert.That(m_rig.Spent([package], [spending], Budget()).Single(), Is.GreaterThanOrEqualTo(Budget()), "the spending package ran out before its last row");
            Assert.That(last.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(last.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
        });
    }

    #endregion

    #region Walk Tests

    [Test]
    public void WalkThatSaysTheMostIsTakenWhateverTheOrderTest()
    {
        var failed = new SmtWalk(new SmtModelCheck(SmtRunWords.FAILED, SmtValidationReasons.ASSERTION_FALSE, "1"), 2, true);
        var ended = new SmtWalk(new SmtModelCheck(SmtRunWords.VALIDATED, "", ""), 3, true);
        var far = new SmtWalk(new SmtModelCheck(SmtRunWords.PARTIAL, SmtRunWords.BUDGET_PACKAGE, ""), 2, false);
        var near = new SmtWalk(new SmtModelCheck(SmtRunWords.PARTIAL, SmtRunWords.BUDGET_PACKAGE, ""), 1, false);

        Assert.Multiple(() =>
        {
            Assert.That(SmtWalk.Better(SmtWalk.Better(near, far), ended), Is.SameAs(ended));
            Assert.That(SmtWalk.Better(SmtWalk.Better(ended, near), far), Is.SameAs(ended));
            Assert.That(SmtWalk.Better(near, far), Is.SameAs(far));
            Assert.That(SmtWalk.Better(far, near), Is.SameAs(far));
            Assert.That(SmtWalk.Better(far, failed), Is.SameAs(failed));
            Assert.That(SmtWalk.Better(null, near), Is.SameAs(near));
        });
    }

    [Test]
    public void ModelThatEndsInAFaultOfTheEvaluatorPaysTheWholeBudgetTest()
    {
        var fault = new SmtValidation { Class = SmtValidationClass.Partial, Reason = SmtValidationReasons.EVALUATOR_ERROR, Steps = 0 };
        var cheap = new SmtValidation { Class = SmtValidationClass.Validated, Steps = 7 };

        Assert.Multiple(() =>
        {
            Assert.That(SmtPackageBudget.Price(fault, 10), Is.EqualTo(SmtPackageBudget.MODEL_STEPS + 10 / SmtPackageBudget.CHARACTERS_PER_STEP + SmtEvaluationBudget.DEFAULT_STEPS));
            Assert.That(SmtPackageBudget.Price(null, 10), Is.EqualTo(SmtPackageBudget.Price(fault, 10)), "a check that threw");
            Assert.That(SmtPackageBudget.Price(cheap, 10), Is.EqualTo(SmtPackageBudget.MODEL_STEPS + 10 / SmtPackageBudget.CHARACTERS_PER_STEP + 7));
        });
    }

    [Test]
    public void BudgetNeverWrapsTest()
    {
        var budget = new SmtPackageBudget();
        budget.Charge(long.MaxValue - 1);
        budget.Charge(long.MaxValue);

        Assert.That(budget.Spent, Is.EqualTo(long.MaxValue));
        Assert.That(budget.HasRoom(long.MaxValue), Is.False);
    }

    #endregion

    #region Tools

    /// <summary>A budget of a package the tests' burning models pass at their third.</summary>
    private static long Budget()
    {
        return (CHECKED - 1) * Price(BURNING) + 1;
    }

    /// <summary>What checking a model that uses up a check's budget costs a package.</summary>
    private static long Price(string model)
    {
        return SmtPackageBudget.MODEL_STEPS + model.Length / SmtPackageBudget.CHARACTERS_PER_STEP + SmtEvaluationBudget.DEFAULT_STEPS;
    }

    /// <summary>A set of problems of one text, each its own row, read with options of a rate, and its one package.</summary>
    private (string Set, SmtOptionsData Options, SmtTaskPackageData Package) Many(int count, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of(Enumerable.Range(0, count).Select(index => JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = $"p{index}", ["smt2"] = NON_NEGATIVE })));
        return (set, options, m_rig.Publish(SmtSettleRig.Read(set, options), options));
    }

    #endregion
}
