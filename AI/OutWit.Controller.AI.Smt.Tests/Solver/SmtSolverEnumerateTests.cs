using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The query <c>enumerate</c> with the real solver module: the models of a problem, told apart
/// by the constants it is projected on, up to a number. Eight queens have 92 placements and the
/// enumeration finds exactly those and says that there are no more; a number of models that is
/// reached leaves the count open; one solution, and none, are told from "one or more". Every
/// model found is checked by the model evaluator. Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverEnumerateTests
{
    #region Constants

    private const int QUEENS = 8;
    private const int PLACEMENTS = 92;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Tests

    [Test]
    public void EightQueensHaveNinetyTwoPlacementsAndNoMoreTest()
    {
        var text = SmtSolverProblems.Queens(QUEENS);
        var problem = SmtSolverProblems.Enumerate("queens", text, 1_000, SmtSolverProblems.QueenNames(QUEENS));

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        TestContext.Out.WriteLine(solved.ToString());
        Assert.That(solved.Words(), Is.EqualTo($"ok sat {PLACEMENTS} complete"), solved.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(solved.Count, Is.EqualTo(PLACEMENTS));
            Assert.That(solved.Models, Has.Count.EqualTo(PLACEMENTS));
            Assert.That(solved.Values, Has.Count.EqualTo(PLACEMENTS));
            Assert.That(solved.Values, Is.Unique, "no placement is found twice: each was ruled out before the next check");
            Assert.That(solved.Checks, Is.EqualTo(PLACEMENTS + 1), "a check a model, and the one that said there is no other");
            Assert.That(solved.LimitReached, Is.False);

            for (var index = 0; index < solved.Models.Count; index++)
            {
                var validation = SmtSolverProblems.Validate(text, solved.Models[index], solved.Values[index]);
                Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), $"model {index}: {validation.Reason} {validation.Detail}\n{solved.Values[index]}");
            }
        });
    }

    [Test]
    public void ANumberOfModelsThatIsReachedLeavesTheCountOpenTest()
    {
        var text = SmtSolverProblems.Queens(QUEENS);
        var problem = SmtSolverProblems.Enumerate("queens", text, 2, SmtSolverProblems.QueenNames(QUEENS));

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat 2 limit"), solved.ToString());
            Assert.That(solved.Complete, Is.False, "two were found; whether there are more was not asked");
            Assert.That(solved.LimitReached, Is.True);
            Assert.That(solved.Checks, Is.EqualTo(2), "no check after the model that filled the number");
            Assert.That(solved.Values, Is.Unique);
            foreach (var (model, values) in solved.Models.Zip(solved.Values))
                Assert.That(SmtSolverProblems.Validate(text, model, values).Class, Is.EqualTo(SmtValidationClass.Validated));
        });
    }

    [Test]
    public void AProblemWithOneSolutionGivesOneAndIsCompleteTest()
    {
        var one = SmtSolverCases.Of("enumerate-one");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat 1 complete"), solved.ToString());
            Assert.That(solved.Values, Is.EqualTo(new[] { "((x 7)\n (y 3))\n" }));
            Assert.That(solved.Checks, Is.EqualTo(2), "the model, and the check that found no second one: the default of two models tells exactly one from more");
            Assert.That(solved.Transcript, Does.Contain("> (assert (not (and (= x 7) (= y 3))))\n> (check-sat)\nunsat\n"));
            Assert.That(SmtSolverProblems.Validate(one.Text, solved.Models[0], solved.Values[0]).Class, Is.EqualTo(SmtValidationClass.Validated));
        });
    }

    [Test]
    public void AnUnsatisfiableProblemGivesNoneAndIsCompleteTest()
    {
        var one = SmtSolverCases.Of("enumerate-none");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unsat 0 complete"), solved.ToString());
            Assert.That(solved.Models, Is.Empty);
            Assert.That(solved.Values, Is.Empty);
            Assert.That(solved.Checks, Is.EqualTo(1));
        });
    }

    [TestCase("enumerate-flags", 4, TestName = "{m}(a bit-vector and a Boolean)")]
    [TestCase("enumerate-colors", 2, TestName = "{m}(a constant of a datatype)")]
    [TestCase("enumerate-queens-6", 4, TestName = "{m}(six queens)")]
    public void ModelsAreToldApartByConstantsOfAnySortTest(string id, int count)
    {
        var one = SmtSolverCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo($"ok sat {count} complete"), solved.ToString());
            Assert.That(solved.Values, Is.Unique);
            foreach (var (model, values) in solved.Models.Zip(solved.Values))
                Assert.That(SmtSolverProblems.Validate(one.Text, model, values).Class, Is.EqualTo(SmtValidationClass.Validated), values);
        });
    }

    [Test]
    public void ModelsThatAgreeOnTheProjectedConstantsAreOneTest()
    {
        // Five queens have ten placements; told apart by the first queen alone there are five.
        var one = SmtSolverCases.Of("enumerate-queens-5-first");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat 5 complete"), solved.ToString());
            Assert.That(solved.Values.Order(), Is.EqualTo(new[] { "((q0 0))\n", "((q0 1))\n", "((q0 2))\n", "((q0 3))\n", "((q0 4))\n" }));
        });
    }

    [Test]
    public void AnEnumerationWhoseNextCheckTheResourceLimitStopsKeepsItsModelsAndIsIncompleteTest()
    {
        var one = SmtSolverCases.Of("enumerate-then-rlimit");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat rlimit 1 incomplete"), solved.ToString());
            Assert.That(solved.Values, Is.EqualTo(new[] { "((easy true))\n" }));
            Assert.That(solved.Checks, Is.EqualTo(2));
            Assert.That(solved.Complete, Is.False, "one model is known; that there is no other is not");
            Assert.That(solved.ReasonText, Is.EqualTo("canceled"));
            Assert.That(SmtSolverProblems.Validate(one.Text, solved.Models[0], solved.Values[0]).Class, Is.EqualTo(SmtValidationClass.Validated));
        });
    }

    #endregion
}
