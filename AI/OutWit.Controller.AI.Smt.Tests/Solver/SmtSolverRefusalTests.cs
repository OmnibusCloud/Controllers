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

    /// <summary>What a node without a sandbox says of a problem its plan took: a fault of the node, no refusal.</summary>
    private const string NOT_REFUSED = "node_fault sandbox_unavailable";

    // The code as a lab reads it: a public word of the contract, written out here.
    private const string PROJECT_SORT_UNSUPPORTED = "project_sort_unsupported";

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

    #region Projection Sort Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Refused))]
    public void AProjectionTheReaderOfASetRefusesIsRefusedByANodeInTheSameWordsTest(string id)
    {
        // A node reads again what travelled to it. A problem a host of this controller would
        // never send (the reader of the set refuses its row) is refused by the node by the same
        // rule, with the same code and the same reason.
        var one = SmtProjectionCases.Of(id);
        var host = Problems.SmtProblemSets.Refuse(one.Row(), PROJECT_SORT_UNSUPPORTED);

        var solved = Solve(one.Problem(), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo($"refused {PROJECT_SORT_UNSUPPORTED}"));
            Assert.That(solved.Message, Is.EqualTo(host.Reason));
            Assert.That(solved.Fuel, Is.Zero, "no session ran");
            Assert.That(solved.Checks, Is.Zero);
            Assert.That(solved.Transcript, Is.Empty);
        });
    }

    [Test]
    public void ANodeRefusesNoProjectionTheReaderOfASetTookTest()
    {
        // The property the two ends are held to: whatever the reader of a set takes, a node's
        // plan takes (a node without a sandbox then says that it has none, which is no refusal),
        // and whatever a node's plan refuses by the rule of the sorts, the reader refuses.
        Assert.Multiple(() =>
        {
            foreach (var one in SmtProjectionCases.All)
            {
                var row = Problems.SmtProblemSets.Read(one.Row()).Rows.Single();
                var node = Solve(one.Problem(), new SmtOptionsData());

                Assert.That(row.IsAccepted, Is.EqualTo(!one.IsRefused), $"{one.Id}: the reader of the set");
                Assert.That(node.Words(), Is.EqualTo(one.IsRefused ? $"refused {PROJECT_SORT_UNSUPPORTED}" : NOT_REFUSED), $"{one.Id}: the node's plan");

                // And what a host sends is what the node took: the row's own problem, as it travels.
                if (row.IsAccepted)
                    Assert.That(Solve(row.Data, new SmtOptionsData()).Words(), Is.EqualTo(NOT_REFUSED), $"{one.Id}: the row the reader made");
            }
        });
    }

    [Test]
    public void ASortMadeAtRandomIsTakenOrRefusedAlikeByBothEndsAndByItsOwnShapeTest()
    {
        // Sorts put together at random from the makers below, each of which either has its
        // argument in a value or does not: the test knows from the shape alone whether a value
        // can hold an element of U, and the reader of a set and a node's plan must both say so.
        const int SORTS = 2_000;
        const int SEED = 20261126;
        const string DECLARATIONS =
            "(declare-sort U 0)(declare-sort P 1)"
            + "(declare-datatype Opt (par (T) ((none) (some (get T)))))"
            + "(declare-datatype Ph (par (T) ((ph (val Bool)))))"
            + "(declare-datatype Pr (par (A B) ((pr (fst A) (snd B)))))"
            + "(declare-datatype Half (par (A B) ((half (only A)))))"
            + "(define-sort Drop (T) Int)(define-sort Same (T) T)(define-sort Tail (A B) (Seq B))";

        var random = new Random(SEED);
        var refused = 0;

        Assert.Multiple(() =>
        {
            for (var number = 0; number < SORTS; number++)
            {
                var (sort, holds) = RandomSort(random, 4);
                var one = new SmtProjectionCase($"random-{number}", DECLARATIONS + $"(declare-const c {sort})", 2, ["c"], "");

                var row = Problems.SmtProblemSets.Read(one.Row()).Rows.Single();
                var node = Solve(one.Problem(), new SmtOptionsData());
                refused += holds ? 1 : 0;

                Assert.That(row.IsAccepted, Is.EqualTo(!holds), $"{sort}: the reader of the set ({row.Refusal?.Code})");
                Assert.That(node.Words(), Is.EqualTo(holds ? $"refused {PROJECT_SORT_UNSUPPORTED}" : NOT_REFUSED), $"{sort}: the node's plan");
            }
        });

        TestContext.Out.WriteLine($"{SORTS} sorts, {refused} refused");
        Assert.That(refused, Is.InRange(SORTS / 5, 4 * SORTS / 5), "the sorts are of both kinds");
    }

    #endregion

    #region Tools

    /// <summary>
    /// A sort made at random, and whether its values can hold an element of an uninterpreted
    /// sort, told from how it was made.
    /// </summary>
    private static (string Sort, bool Holds) RandomSort(Random random, int depth)
    {
        if (depth == 0 || random.Next(4) == 0)
        {
            return random.Next(5) switch
            {
                0 => ("U", true),
                1 => ("Int", false),
                2 => ("Bool", false),
                3 => ("(_ BitVec 8)", false),
                _ => ("String", false)
            };
        }

        var (first, firstHolds) = RandomSort(random, depth - 1);
        var (second, secondHolds) = RandomSort(random, depth - 1);
        return random.Next(11) switch
        {
            0 => ($"(Array {first} {second})", firstHolds || secondHolds),
            1 => ($"(Seq {first})", firstHolds),
            2 => ($"(Opt {first})", firstHolds),
            3 => ($"(Ph {first})", false),
            4 => ($"(Pr {first} {second})", firstHolds || secondHolds),
            5 => ($"(Half {first} {second})", firstHolds),
            6 => ($"(Drop {first})", false),
            7 => ($"(Same {first})", firstHolds),
            8 => ($"(Tail {first} {second})", secondHolds),
            9 => ($"(P {first})", true),
            _ => ($"(-> {first} {second})", firstHolds || secondHolds)
        };
    }

    /// <summary>Solves as a machine without a sandbox would: what is refused is refused before the sandbox is asked for.</summary>
    private static SmtSolved Solve(SmtProblemData problem, SmtOptionsData options)
    {
        return SmtSolverRig.Solve(problem, options, new SmtSolverSetup { WithoutSandbox = NO_SANDBOX });
    }

    #endregion
}
