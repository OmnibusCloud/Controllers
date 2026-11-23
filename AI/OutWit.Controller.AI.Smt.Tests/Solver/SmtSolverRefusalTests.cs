using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// What the solver refuses before any session starts: options out of their range (a resource
/// limit the solver's parameter cannot hold never reaches it), a profile the controller does not
/// have, a problem whose text is not what it was said to be, a text or a term this machine's own
/// sanitizer does not take, a query without what it needs. A refusal is an outcome, never an
/// exception, and is the same on a machine that has no sandbox: these tests need no module.
/// </summary>
[TestFixture]
public sealed class SmtSolverRefusalTests
{
    #region Constants

    private const string PROBLEM = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 10))";
    private const string NO_SANDBOX = "a machine without a sandbox";

    #endregion

    #region Options Tests

    [Test]
    public void AResourceLimitAboveThirtyTwoBitsNeverReachesTheSolverTest()
    {
        // Given such a limit the solver reports an error and then runs the check with no limit at all.
        var solved = Solve(SmtSolverProblems.Check("limit", PROBLEM), new SmtOptionsData { ResourceLimit = 4_294_967_296 });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("refused options_invalid"));
            Assert.That(solved.Message, Is.EqualTo("'rlimit' must be between 1 and 4294967295"));
            Assert.That(solved.Fuel, Is.Zero, "no session ran");
            Assert.That(solved.Transcript, Is.Empty);
            Assert.That(solved.Checks, Is.Zero);
        });
    }

    [Test]
    public void TheGreatestResourceLimitTheSolverTakesIsTakenTest()
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("limit", PROBLEM), new SmtOptionsData { ResourceLimit = 4_294_967_295 }, ["", "unsat\n", "(:rlimit-count 5)\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok unsat"));
            Assert.That(held.Sent[0], Does.EndWith("(set-option :rlimit 4294967295)\n"));
        });
    }

    [Test]
    public void EveryOptionOutOfItsRangeIsNamedInTheRefusalTest()
    {
        var solved = Solve(SmtSolverProblems.Check("options", PROBLEM), new SmtOptionsData { HardCapSeconds = 601, MemoryMegabytes = -1, Seed = -5 });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("refused options_invalid"));
            Assert.That(solved.Message, Is.EqualTo("'seed' must be between 0 and 2147483647; 'hard_cap_s' must be between 1 and 600; 'memory_mb' must be between 1 and 4096"));
        });
    }

    [Test]
    public void AnUnknownProfileIsRefusedByItsNameTest()
    {
        var solved = Solve(SmtSolverProblems.Check("profile", PROBLEM), new SmtOptionsData { Profile = "z3-4.8.12" });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("refused profile_unknown"));
            Assert.That(solved.Message, Is.EqualTo("'profile' names 'z3-4.8.12', which this controller does not have: its profile is z3-5.4.1"));
            Assert.That(solved.Fuel, Is.Zero);
        });
    }

    [Test]
    public void TheNameOfAnUnknownProfileIsShownShortAndWithoutControlCharactersTest()
    {
        var solved = Solve(SmtSolverProblems.Check("profile", PROBLEM), new SmtOptionsData { Profile = "a\nb\t" + new string('c', 500) });

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("refused profile_unknown"));
            Assert.That(solved.Message, Does.StartWith("'profile' names 'a b cccc"));
            Assert.That(solved.Message, Has.Length.LessThan(200));
        });
    }

    [TestCase("")]
    [TestCase("z3-5.4.1")]
    public void TheControllersOwnProfileIsTakenByItsNameAndByNoNameTest(string profile)
    {
        var solved = Solve(SmtSolverProblems.Check("profile", PROBLEM), new SmtOptionsData { Profile = profile });

        Assert.That(solved.Words(), Is.EqualTo("node_fault sandbox_unavailable"), "the options were taken: the session got as far as the sandbox, which this test does not give it");
    }

    #endregion

    #region Problem Tests

    [Test]
    public void AProblemWhoseTextHashesToAnotherNameIsRefusedTest()
    {
        var problem = SmtSolverProblems.Check("forged", PROBLEM);
        var honest = problem.Hash;
        problem.Text = problem.Text.Replace("10", "11");

        var solved = Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("refused hash_mismatch"));
            Assert.That(solved.Message, Does.Contain("it is not the problem it was said to be"));
            Assert.That(solved.Message, Does.Not.Contain(honest), "the name the problem came with is not repeated: it is whatever a sender wrote");
            Assert.That(solved.Fuel, Is.Zero);
        });
    }

    [Test]
    public void ATextThatIsWrittenAnotherWayButIsTheSameProblemIsTakenTest()
    {
        // The hash names the canonical form: comments, blanks and bars that are not needed do not move it.
        var problem = SmtSolverProblems.Check("spelled", PROBLEM);
        problem.Text = "; a comment\n" + PROBLEM.Replace(")(", ")\n  (").Replace("declare-const x", "declare-const |x|");

        var solved = Solve(problem, new SmtOptionsData());

        Assert.That(solved.Words(), Is.EqualTo("node_fault sandbox_unavailable"), "taken: it got as far as the sandbox");
    }

    [TestCase("(declare-const x Int)(push 1)(assert (> x 1))", SmtRefusalCodes.COMMAND_NOT_ALLOWED)]
    [TestCase("(declare-const x Int)(set-option :rlimit 0)", SmtRefusalCodes.COMMAND_NOT_ALLOWED)]
    [TestCase("(declare-const x Int)(assert (> x 1)", SmtRefusalCodes.SYNTAX_ERROR)]
    [TestCase("(declare-const x Int)(assert (> x -5))", SmtRefusalCodes.AMBIGUOUS_TOKEN)]
    public void ATextThisMachinesSanitizerDoesNotTakeIsRefusedWithTheSanitizersCodeTest(string text, string code)
    {
        // Whatever sent the problem is not trusted to have looked at it: the hash below is the
        // one the text really has, and the text is refused all the same.
        var problem = new SmtProblemData { Id = "raw", Query = SmtQuery.Check, Text = text, Hash = new string('0', 64) };

        var solved = Solve(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo($"refused {code}"));
            Assert.That(solved.Refusal, Does.StartWith(code));
            Assert.That(solved.Message, Is.Not.Empty);
        });
    }

    [Test]
    public void ATermThisMachinesSanitizerDoesNotTakeIsRefusedWithItsFieldTest()
    {
        var values = SmtSolverProblems.Check("values", PROBLEM, "x");
        values.Values.Add("(+ x -5)");
        var goal = SmtSolverProblems.Prove("goal", PROBLEM, "(> x 1)");
        goal.Goal = "(> x 1) (> y 1)";
        var project = SmtSolverProblems.Enumerate("project", PROBLEM, 2, "x");
        project.Project.Add("|oc!x|");

        var refusedValues = Solve(values, new SmtOptionsData());
        var refusedGoal = Solve(goal, new SmtOptionsData());
        var refusedProject = Solve(project, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(refusedValues.Words(), Is.EqualTo($"refused {SmtRefusalCodes.AMBIGUOUS_TOKEN}"));
            Assert.That(refusedValues.Message, Does.StartWith("'values[1]': "));
            Assert.That(refusedGoal.Words(), Is.EqualTo($"refused {SmtRefusalCodes.NOT_A_TERM}"));
            Assert.That(refusedGoal.Message, Does.StartWith("'goal': "));
            Assert.That(refusedProject.Words(), Is.EqualTo($"refused {SmtRefusalCodes.RESERVED_PREFIX}"));
            Assert.That(refusedProject.Message, Does.StartWith("'project[1]': "));
        });
    }

    #endregion

    #region Query Tests

    [Test]
    public void AQueryWithoutWhatItNeedsIsRefusedTest()
    {
        var noGoal = SmtSolverProblems.Prove("goal", PROBLEM, "(> x 1)");
        noGoal.Goal = "";
        var noProjection = SmtSolverProblems.Enumerate("project", PROBLEM, 2, "x");
        noProjection.Project.Clear();
        var noQuery = SmtSolverProblems.Check("query", PROBLEM);
        noQuery.Query = (SmtQuery)7;

        Assert.Multiple(() =>
        {
            Assert.That(Solve(noGoal, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_MISSING}"));
            Assert.That(Solve(noProjection, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_MISSING}"));
            Assert.That(Solve(noQuery, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_INVALID}"));
        });
    }

    [Test]
    public void AProjectionOnWhatIsNoDeclaredConstantOnceIsRefusedTest()
    {
        var unknown = SmtSolverProblems.Enumerate("project", PROBLEM, 2, "x");
        unknown.Project.Add("z");
        var twice = SmtSolverProblems.Enumerate("project", PROBLEM, 2, "x", "y");
        twice.Project.Add("x");
        var term = SmtSolverProblems.Enumerate("project", PROBLEM, 2, "x");
        term.Project.Add("(+ x y)");
        var tooMany = SmtSolverProblems.Enumerate("project", PROBLEM, 1_001, "x");

        Assert.Multiple(() =>
        {
            Assert.That(Solve(unknown, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.UNKNOWN_CONSTANT}"));
            Assert.That(Solve(twice, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_INVALID}"));
            Assert.That(Solve(term, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_INVALID}"));
            Assert.That(Solve(tooMany, new SmtOptionsData()).Words(), Is.EqualTo($"refused {SmtRefusalCodes.FIELD_INVALID}"));
        });
    }

    #endregion

    #region Tools

    /// <summary>Solves as a machine without a sandbox would: what is refused is refused before the sandbox is asked for.</summary>
    private static SmtSolved Solve(SmtProblemData problem, SmtOptionsData options)
    {
        return SmtSolverRig.Solve(problem, options, new SmtSolverSetup { WithoutSandbox = NO_SANDBOX });
    }

    #endregion
}
