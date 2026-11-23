using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The query <c>check</c> with the real solver module in the sandbox: a satisfiable problem of
/// each logic the model evaluator covers, whose model the evaluator validates; an unsatisfiable
/// one; each kind of <c>unknown</c> a small budget reaches; the values of terms; the seed; an
/// error of the solver's with its message. Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverCheckTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Sat Tests

    [TestCase("check-lia-sat", TestName = "{m}(linear integers)")]
    [TestCase("check-lra-sat", TestName = "{m}(linear reals)")]
    [TestCase("check-bv-sat", TestName = "{m}(bit-vectors)")]
    [TestCase("check-bv-factors-sat", TestName = "{m}(bit-vectors, a search)")]
    [TestCase("check-uf-sat", TestName = "{m}(uninterpreted functions)")]
    [TestCase("check-ax-sat", TestName = "{m}(arrays)")]
    [TestCase("check-alia-sat", TestName = "{m}(arrays of integers)")]
    [TestCase("check-dt-sat", TestName = "{m}(datatypes)")]
    [TestCase("check-uflia-sat", TestName = "{m}(uninterpreted functions over integers)")]
    [TestCase("check-strings-sat", TestName = "{m}(basic strings)")]
    public void ASatisfiableProblemGivesAModelTheEvaluatorValidatesTest(string id)
    {
        var one = SmtSolverCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.That(solved.Words(), Is.EqualTo("ok sat"), solved.ToString());
        Assert.That(solved.Models, Has.Count.EqualTo(1));

        var validation = SmtSolverProblems.Validate(one.Text, solved.Models[0]);
        Assert.Multiple(() =>
        {
            Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), $"{validation.Reason} {validation.Detail}\n{solved.Models[0]}");
            Assert.That(validation.True, Is.EqualTo(validation.Assertions), "every assertion evaluated to true");
            Assert.That(solved.Values, Is.Empty, "no values were asked");
            Assert.That(solved.Checks, Is.EqualTo(1));
            Assert.That(solved.Fuel, Is.GreaterThan(0));
            Assert.That(solved.PeakMemoryBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AStatusThatDisagreesWithTheVerdictDoesNotReachTheSolverTest()
    {
        // The solver acts on a :status: one that disagrees with its verdict makes it report an
        // error instead of answering. No set-info of a problem is given to it, so a problem
        // with a wrong expectation is solved like any other, and the expectation is compared
        // by whoever reads the outcome.
        var problem = SmtSolverProblems.Check("status", "(set-info :status unsat)" + SmtSolverCases.ONE_SOLUTION);

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(problem.Text, Does.Contain("(set-info :status unsat)"), "the problem as it travels keeps the line");
            Assert.That(problem.Expected, Is.EqualTo(SmtProblemStatus.Unsat));
            Assert.That(solved.Words(), Is.EqualTo("ok sat"), solved.ToString());
            Assert.That(solved.Transcript, Does.Not.Contain("set-info"));
        });
    }

    [Test]
    public void AProblemWithoutAssertionsIsSatisfiedByTheEmptyModelTest()
    {
        var one = SmtSolverCases.Of("check-empty-sat");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat"));
            Assert.That(solved.Models, Is.EqualTo(new[] { "(\n)\n" }));
        });
    }

    #endregion

    #region Unsat Tests

    [TestCase("check-lia-unsat")]
    [TestCase("check-lra-unsat")]
    [TestCase("check-bv-unsat")]
    [TestCase("check-uf-unsat")]
    [TestCase("check-ax-unsat")]
    [TestCase("check-dt-unsat")]
    public void AnUnsatisfiableProblemIsUnsatWithoutAModelTest(string id)
    {
        var one = SmtSolverCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unsat"), solved.ToString());
            Assert.That(solved.Models, Is.Empty);
            Assert.That(solved.Transcript, Does.Not.Contain("get-model"), "nothing asks for a model after unsat");
            Assert.That(solved.Checks, Is.EqualTo(1));
        });
    }

    #endregion

    #region Unknown Tests

    [Test]
    public void ASmallResourceLimitOnAHardProblemIsUnknownByTheLimitWithTheCounterAtTheLimitTest()
    {
        const long SMALL = 50_000;
        const long LARGER = 120_000;
        var pigeons = SmtSolverProblems.Check("pigeonhole", SmtSolverProblems.Pigeonhole(10));

        var small = SmtSolverRig.Solve(pigeons, new SmtOptionsData { ResourceLimit = SMALL });
        var larger = SmtSolverRig.Solve(pigeons, new SmtOptionsData { ResourceLimit = LARGER });

        TestContext.Out.WriteLine($"limit {SMALL}: {small}\nlimit {LARGER}: {larger}");
        Assert.Multiple(() =>
        {
            Assert.That(small.Words(), Is.EqualTo("ok unknown rlimit"));
            Assert.That(larger.Words(), Is.EqualTo("ok unknown rlimit"));
            Assert.That(small.ReasonText, Is.EqualTo("canceled"), "the solver's own word for a check its limit stopped");

            // The counter runs over the whole session; a check may add its limit to it and no
            // more, and the solver leaves the counter there when the limit stopped the check. So
            // the counter stands at what the problem's own commands had counted, plus the limit.
            var before = small.ResourceCount - SMALL;
            Assert.That(before, Is.InRange(0, SMALL / 10), "what declaring and asserting the problem counted");
            Assert.That(larger.ResourceCount - LARGER, Is.EqualTo(before), "the counter is at the limit, whichever limit it is");
            Assert.That(larger.Fuel, Is.GreaterThan(small.Fuel), "more of the solver's work under the larger limit");
            Assert.That(small.Models, Is.Empty);
        });
    }

    [Test]
    public void AProblemTheSolversProcedureIsNotCompleteForIsUnknownByItsOwnWordTest()
    {
        var one = SmtSolverCases.Of("check-strings-incomplete");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok unknown incomplete"), solved.ToString());
            Assert.That(solved.ReasonText, Does.Contain("incomplete (theory seq)"));
            Assert.That(solved.ResourceCount, Is.LessThan(1_000_000), "far below the limit: the solver gave up by itself");
        });
    }

    #endregion

    #region Values Tests

    [Test]
    public void TheValuesOfTheTermsARowAsksForAreGivenAfterTheModelTest()
    {
        var problem = SmtSolverProblems.Check("values", SmtSolverCases.ONE_SOLUTION, "x", "(- x y)", "(* 2 y)");

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.That(solved.Words(), Is.EqualTo("ok sat"), solved.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(solved.Values, Is.EqualTo(new[] { "((x 7)\n ((- x y) 4)\n ((* 2 y) 6))\n" }), "each term as it was asked, and its value");
            Assert.That(solved.Models, Has.Count.EqualTo(1));
            Assert.That(solved.Transcript, Does.Contain("> (get-model)\n").And.Contain("> (get-value (x (- x y) (* 2 y)))\n"));
            Assert.That(solved.Transcript.IndexOf("get-model", StringComparison.Ordinal), Is.LessThan(solved.Transcript.IndexOf("get-value", StringComparison.Ordinal)));
            Assert.That(SmtSolverProblems.Validate(SmtSolverCases.ONE_SOLUTION, solved.Models[0], solved.Values[0]).Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That(SmtSolverProblems.Validate(SmtSolverCases.ONE_SOLUTION, null, "((x 7)\n (y 3))").Class, Is.EqualTo(SmtValidationClass.Validated), "the values alone are a witness too");
        });
    }

    [Test]
    public void ATermWhoseValueIsAskedAndThatTheSolverDoesNotTakeIsAnInputErrorTest()
    {
        var one = SmtSolverCases.Of("check-value-undeclared");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"));
            Assert.That(solved.Message, Does.EndWith("unknown constant w"));
            Assert.That(solved.Verdict, Is.EqualTo("none"), "the sat the check gave is not claimed by an outcome that is no answer");
        });
    }

    #endregion

    #region Seed Tests

    [Test]
    public void TheSeedOfTheOptionsReachesTheSolverTest()
    {
        var problem = SmtSolverCases.Of("check-bv-factors-sat").Problem;

        var zero = SmtSolverRig.Solve(problem, new SmtOptionsData());
        var seven = SmtSolverRig.Solve(problem, new SmtOptionsData { Seed = 7 });
        var again = SmtSolverRig.Solve(problem, new SmtOptionsData { Seed = 7 });
        var greatest = SmtSolverRig.Solve(problem, new SmtOptionsData { Seed = int.MaxValue });

        Assert.Multiple(() =>
        {
            Assert.That(zero.Words(), Is.EqualTo("ok sat"));
            Assert.That(seven.Words(), Is.EqualTo("ok sat"));
            Assert.That(greatest.Words(), Is.EqualTo("ok sat"), $"the greatest seed the options take is one the solver takes: {greatest}");
            Assert.That(seven.Models[0], Is.Not.EqualTo(zero.Models[0]), "another seed, another pair of factors");
            Assert.That(seven.Fuel, Is.Not.EqualTo(zero.Fuel));
            Assert.That(again.Canonical, Is.EqualTo(seven.Canonical), "the same seed, the same outcome");
            Assert.That(SmtSolverProblems.Validate(SmtSolverCases.BV_FACTORS, seven.Models[0]).Class, Is.EqualTo(SmtValidationClass.Validated));
        });
    }

    #endregion

    #region Input Error Tests

    [Test]
    public void AnUndeclaredSymbolIsAnInputErrorWithTheSolversMessageTest()
    {
        var one = SmtSolverCases.Of("check-undeclared");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"), solved.ToString());
            Assert.That(solved.Message, Does.Match(@"^line \d+ column \d+: unknown constant y$"));
            Assert.That(solved.Checks, Is.Zero, "no check is sent for a problem the solver did not take");
            Assert.That(solved.Transcript, Does.EndWith("(error \"" + solved.Message + "\")\n"));
            Assert.That(solved.Models, Is.Empty);
        });
    }

    [Test]
    public void ALongMessageOfTheSolverIsCutTest()
    {
        var name = new string('y', 2_000);
        var problem = SmtSolverProblems.Check("long", $"(set-logic QF_LIA)(declare-const x Int)(assert (> {name} x))");

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"));
            Assert.That(solved.Message, Has.Length.EqualTo(512));
            Assert.That(solved.Message, Does.Contain("unknown constant yyyy"));
        });
    }

    [Test]
    public void AnAssertionTheLogicDoesNotAllowIsAnInputErrorTest()
    {
        var one = SmtSolverCases.Of("check-nonlinear-in-linear");

        var solved = SmtSolverRig.Solve(one.Problem, one.Options);

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"));
            Assert.That(solved.Message, Does.EndWith("logic does not support nonlinear arithmetic"));
        });
    }

    [Test]
    public void ALogicTheSolverDoesNotKnowIsAnInputErrorTest()
    {
        // The solver says "unsupported", names the logic on its other stream and would go on
        // under no logic at all: a problem is not solved as another problem than it says it is.
        var problem = SmtSolverProblems.Check("logic", "(set-logic QF_NO_SUCH_LOGIC)(declare-const x Int)(assert (> x 1))");

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("input_error"));
            Assert.That(solved.Message, Is.EqualTo("unsupported"));
            Assert.That(solved.Transcript, Does.Contain("ignoring unsupported logic QF_NO_SUCH_LOGIC"), "what the solver wrote to its other stream is in the transcript");
        });
    }

    #endregion

    #region Start Tests

    [Test]
    public void ASessionStartsInAFewMillisecondsAndBurnsTheSameFuelEveryTimeTest()
    {
        const int RUNS = 25;
        var one = SmtSolverCases.Of("check-empty-sat");

        var runs = Enumerable.Range(0, RUNS).Select(_ => SmtSolverRig.Solve(one.Problem, one.Options)).ToList();
        var walls = runs.Select(run => run.WallMs).OrderBy(wall => wall).ToList();

        // The time is this machine's and is printed, not held: a fresh instance, the solver's own
        // start, one check of nothing, the end.
        TestContext.Out.WriteLine($"a session of the empty problem, {RUNS} times: wall time median {walls[RUNS / 2]} ms, least {walls[0]} ms, most {walls[^1]} ms; fuel {runs[0].Fuel}; peak memory {runs[0].PeakMemoryBytes} bytes");
        Assert.Multiple(() =>
        {
            Assert.That(runs.Select(run => run.Canonical).Distinct().Count(), Is.EqualTo(1), "every run is the same outcome");
            Assert.That(runs[0].Fuel, Is.LessThan(50_000_000), "the solver's start and a check of nothing: far below a hundredth of a second of the reference rate");
        });
    }

    #endregion
}
