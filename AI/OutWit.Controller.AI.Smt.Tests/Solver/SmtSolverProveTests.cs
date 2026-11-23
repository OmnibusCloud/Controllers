using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The query <c>prove</c> with the real solver module: the goal follows from the problem exactly
/// when the problem with the negated goal has no model. A goal that follows is proved; one that
/// does not comes back with a counterexample, a model of the problem in which the goal is false,
/// which the model evaluator checks against the problem with the negated goal. Needs the module
/// (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverProveTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Tests

    [TestCase("prove-valid", TestName = "{m}(integers)")]
    [TestCase("prove-bv-valid", TestName = "{m}(bit-vectors)")]
    [TestCase("prove-array-valid", TestName = "{m}(arrays)")]
    public void AGoalThatFollowsIsProvedTest(string id)
    {
        var one = SmtSolverCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unsat proved"), solved.ToString());
            Assert.That(solved.Models, Is.Empty);
            Assert.That(solved.Transcript, Does.Contain($"> (assert (not {one.Problem.Goal}))\n"), "the negated goal is asserted with the problem");
        });
    }

    [TestCase("prove-invalid", TestName = "{m}(integers)")]
    [TestCase("prove-uf-invalid", TestName = "{m}(uninterpreted functions)")]
    public void AGoalThatDoesNotFollowGivesACounterexampleTheEvaluatorValidatesTest(string id)
    {
        var one = SmtSolverCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.That(solved.Words(), Is.EqualTo("ok sat counterexample"), solved.ToString());
        Assert.That(solved.Models, Has.Count.EqualTo(1));

        // The counterexample satisfies the hypotheses and makes the goal false: it is a model of
        // the problem with the negated goal, and of the problem alone.
        var refuted = SmtSolverProblems.Validate($"{one.Text}\n(assert (not {one.Problem.Goal}))", solved.Models[0]);
        var hypotheses = SmtSolverProblems.Validate(one.Text, solved.Models[0]);
        var goal = SmtSolverProblems.Validate($"{one.Text}\n(assert {one.Problem.Goal})", solved.Models[0]);

        Assert.Multiple(() =>
        {
            Assert.That(refuted.Class, Is.EqualTo(SmtValidationClass.Validated), $"{refuted.Reason} {refuted.Detail}\n{solved.Models[0]}");
            Assert.That(hypotheses.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That(goal.Class, Is.EqualTo(SmtValidationClass.Failed), "the goal itself is false under the counterexample");
        });
    }

    [Test]
    public void AGoalTheSolverDoesNotTakeIsAnInputErrorTest()
    {
        var problem = SmtSolverProblems.Prove("goal", SmtSolverCases.ABOVE_ONE, "(> x unknown_name)");

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"));
            Assert.That(solved.Message, Does.EndWith("unknown constant unknown_name"));
            Assert.That(solved.Proof, Is.EqualTo("none"));
        });
    }

    #endregion
}
