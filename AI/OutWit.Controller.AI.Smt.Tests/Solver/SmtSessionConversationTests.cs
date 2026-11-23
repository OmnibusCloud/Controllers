using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// What a session sends, to the letter, held with a solver that is a script
/// (<see cref="SmtSessionScript"/>): the options, the problem as the sanitizer gave it for the
/// solver, the negated goal of a proof and the resource limit first, answered by nothing; then
/// one command at each of the solver's reads, decided by the answer before it. A check asks the
/// model after <c>sat</c> and the reason after <c>unknown</c>; an enumeration asks the values of
/// its constants and the model, rules the model out and checks again.
/// </summary>
[TestFixture]
public sealed class SmtSessionConversationTests
{
    #region Constants

    private const string PROBLEM = """
        (set-logic QF_LIA)
        (set-info :status sat)
        (set-info :source |a problem of the tests|)
        (declare-const x Int)
        (declare-const y Int)
        (assert (= (+ x y) 10))
        (check-sat)
        (get-model)
        """;

    private const string SOLVER_TEXT = "(set-logic QF_LIA)\n(declare-const x Int)\n(declare-const y Int)\n(assert (= (+ x y) 10))\n";

    private const string OPTIONS = "(set-option :print-success false)\n(set-option :produce-models true)\n(set-option :smt.random_seed 0)\n(set-option :sat.random_seed 0)\n";
    private const string LIMIT = "(set-option :rlimit 150000000)\n";

    private const string CHECK = "(check-sat)\n";
    private const string STATISTICS = "(get-info :all-statistics)\n";
    private const string REASON = "(get-info :reason-unknown)\n";
    private const string MODEL = "(get-model)\n";

    private const string SILENT = "";
    private const string SAT = "sat\n";
    private const string UNSAT = "unsat\n";
    private const string UNKNOWN = "unknown\n";
    private const string COUNTED = "(:max-memory 9.29\n :rlimit-count 223)\n";
    private const string A_MODEL = "(\n  (define-fun x () Int\n    7)\n  (define-fun y () Int\n    3)\n)\n";

    #endregion

    #region Check Tests

    [Test]
    public void ACheckSendsTheOptionsTheProblemAndTheLimitAndThenOneCommandAtEachReadTest()
    {
        var problem = SmtSolverProblems.Check("check", PROBLEM, "x", "(+ x y)");
        const string VALUES = "((x 7)\n ((+ x y) 10))\n";

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, A_MODEL, VALUES]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent, Is.EqualTo(new[] { OPTIONS + SOLVER_TEXT + LIMIT, CHECK, STATISTICS, MODEL, "(get-value (x (+ x y)))\n" }));
            Assert.That(held.Ended, Is.True, "the session ended the input itself");
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok sat"));
            Assert.That(held.Outcome.Models, Is.EqualTo(new[] { A_MODEL }), "the model is the answer of get-model, byte for byte");
            Assert.That(held.Outcome.Values, Is.EqualTo(new[] { VALUES }), "the values are the answer of get-value, byte for byte");
            Assert.That(held.Outcome.Checks, Is.EqualTo(1));
            Assert.That(held.Outcome.ResourceCount, Is.EqualTo(223));
        });
    }

    [Test]
    public void NoSetInfoAndNoCommandOfTheProblemsOwnThatAsksReachesTheSolverTest()
    {
        var problem = SmtSolverProblems.Check("check", PROBLEM);

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, UNSAT, COUNTED]);

        Assert.Multiple(() =>
        {
            Assert.That(problem.Text, Does.Contain("set-info"), "the problem as it travels keeps its set-info lines");
            Assert.That(held.Sent[0], Does.Not.Contain("set-info"), "the solver acts on :status and :smt-lib-version: it is given none");
            Assert.That(held.Sent[0], Does.Not.Contain("a problem of the tests"));
            Assert.That(held.Sent.Count(sent => sent.Contains("check-sat")), Is.EqualTo(1), "the problem's own check-sat was stripped: the query decides what runs");
            Assert.That(string.Concat(held.Sent), Does.Not.Contain("get-model"), "and nothing asks for a model after unsat");
        });
    }

    [Test]
    public void TheSeedAndTheLimitOfTheOptionsAreInWhatIsSentTest()
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData { Seed = 12345, ResourceLimit = 4_294_967_295 }, [SILENT, UNSAT, COUNTED]);

        Assert.That(held.Sent[0], Is.EqualTo(
            "(set-option :print-success false)\n(set-option :produce-models true)\n(set-option :smt.random_seed 12345)\n(set-option :sat.random_seed 12345)\n"
            + SOLVER_TEXT + "(set-option :rlimit 4294967295)\n"));
    }

    [Test]
    public void AfterUnsatNothingMoreIsAskedThanTheStatisticsTest()
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), [SILENT, UNSAT, COUNTED]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent.Skip(1), Is.EqualTo(new[] { CHECK, STATISTICS }));
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok unsat"));
            Assert.That(held.Outcome.Models, Is.Empty);
        });
    }

    [Test]
    public void AfterUnknownTheReasonIsAskedTest()
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), [SILENT, UNKNOWN, COUNTED, "(:reason-unknown \"(incomplete (theory arithmetic))\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent.Skip(1), Is.EqualTo(new[] { CHECK, STATISTICS, REASON }));
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok unknown incomplete"));
            Assert.That(held.Outcome.ReasonText, Is.EqualTo("(incomplete (theory arithmetic))"));
        });
    }

    #endregion

    #region Enumerate Tests

    [Test]
    public void AnEnumerationRulesEachModelOutByTheValuesOfItsConstantsAndChecksAgainTest()
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x", "y");
        const string FIRST = "((x 7)\n (y 3))\n";
        const string SECOND = "((x (- 2))\n (y 12))\n";

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(),
            [SILENT, SAT, COUNTED, FIRST, A_MODEL, SILENT, SAT, COUNTED, SECOND, A_MODEL, SILENT, UNSAT, COUNTED]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent.Skip(1), Is.EqualTo(new[]
            {
                CHECK, STATISTICS, "(get-value (x y))\n", MODEL, "(assert (not (and (= x 7) (= y 3))))\n",
                CHECK, STATISTICS, "(get-value (x y))\n", MODEL, "(assert (not (and (= x (- 2)) (= y 12))))\n",
                CHECK, STATISTICS
            }));
            Assert.That(held.Ended, Is.True);
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok sat 2 complete"), "the last check said unsat: the count is exact");
            Assert.That(held.Outcome.Values, Is.EqualTo(new[] { FIRST, SECOND }));
            Assert.That(held.Outcome.Models, Has.Count.EqualTo(2));
            Assert.That(held.Outcome.Checks, Is.EqualTo(3));
        });
    }

    [Test]
    public void AnEnumerationStopsAtItsNumberOfModelsWithoutAnotherCheckTest()
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 1, "x");

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, "((x 7))\n", A_MODEL]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent.Skip(1), Is.EqualTo(new[] { CHECK, STATISTICS, "(get-value (x))\n", MODEL }), "the model that fills the number is not ruled out: nothing is looked for after it");
            Assert.That(held.Ended, Is.True);
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok sat 1 limit"), "one model found is not 'exactly one'");
            Assert.That(held.Outcome.Complete, Is.False);
        });
    }

    [Test]
    public void AnEnumerationThatEndsInUnknownIsIncompleteWithTheReasonTest()
    {
        var problem = SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x");
        const string AT_THE_LIMIT = "(:rlimit-count 150000223)\n";

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(),
            [SILENT, SAT, COUNTED, "((x 7))\n", A_MODEL, SILENT, UNKNOWN, AT_THE_LIMIT, "(:reason-unknown \"canceled\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(held.Sent[^1], Is.EqualTo(REASON));
            Assert.That(held.Outcome.Words(), Is.EqualTo("ok sat rlimit 1 incomplete"), "the second check spent its whole limit: the counter moved from 223 to 150,000,223");
            Assert.That(held.Outcome.ResourceCount, Is.EqualTo(150_000_223));
        });
    }

    [Test]
    public void AnEnumerationWithoutAModelIsUnsatAndCompleteTest()
    {
        var held = SmtSessionScript.Hold(SmtSolverProblems.Enumerate("enumerate", PROBLEM, 10, "x"), new SmtOptionsData(), [SILENT, UNSAT, COUNTED]);

        Assert.That(held.Outcome.Words(), Is.EqualTo("ok unsat 0 complete"));
    }

    #endregion

    #region Prove Tests

    [Test]
    public void AProofAssertsTheNegatedGoalWithTheProblemTest()
    {
        var problem = SmtSolverProblems.Prove("prove", PROBLEM, "(> (+ x y) 9)");

        var proved = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, UNSAT, COUNTED]);
        var refuted = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, A_MODEL]);
        var open = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, UNKNOWN, COUNTED, "(:reason-unknown \"incomplete\")\n"]);

        Assert.Multiple(() =>
        {
            Assert.That(proved.Sent[0], Is.EqualTo(OPTIONS + SOLVER_TEXT + "(assert (not (> (+ x y) 9)))\n" + LIMIT));
            Assert.That(proved.Outcome.Words(), Is.EqualTo("ok unsat proved"));
            Assert.That(refuted.Sent.Skip(1), Is.EqualTo(new[] { CHECK, STATISTICS, MODEL }));
            Assert.That(refuted.Outcome.Words(), Is.EqualTo("ok sat counterexample"));
            Assert.That(refuted.Outcome.Models, Is.EqualTo(new[] { A_MODEL }));
            Assert.That(open.Outcome.Words(), Is.EqualTo("ok unknown incomplete"), "a proof without a verdict is neither proved nor refuted");
        });
    }

    #endregion

    #region Transcript Tests

    [Test]
    public void TheTranscriptHoldsTheCommandsAndTheAnswersWithoutTheProblemsTextTest()
    {
        var problem = SmtSolverProblems.Check("check", PROBLEM);

        var held = SmtSessionScript.Hold(problem, new SmtOptionsData(), [SILENT, SAT, COUNTED, A_MODEL]);

        Assert.That(held.Outcome.Transcript, Is.EqualTo(
            "> (set-option :print-success false)\n> (set-option :produce-models true)\n> (set-option :smt.random_seed 0)\n> (set-option :sat.random_seed 0)\n"
            + $"; the problem: 4 commands, SHA-256 {problem.Hash}\n"
            + "> (set-option :rlimit 150000000)\n> (check-sat)\nsat\n> (get-info :all-statistics)\n" + COUNTED + "> (get-model)\n" + A_MODEL));
    }

    [Test]
    public void TheTranscriptKeepsTheLastEightKilobytesOfALongSessionTest()
    {
        var large = "(\n" + string.Concat(Enumerable.Range(0, 5000).Select(index => $"  (define-fun c{index} () Int\n    {index})\n")) + ")\n";

        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), [SILENT, SAT, COUNTED, large]);

        Assert.Multiple(() =>
        {
            Assert.That(large, Has.Length.GreaterThan(100_000));
            Assert.That(held.Outcome.Models, Is.EqualTo(new[] { large }), "the model itself is whole");
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(held.Outcome.Transcript), Is.EqualTo(8192));
            Assert.That(large, Does.EndWith(held.Outcome.Transcript), "the end of the last answer");
        });
    }

    [Test]
    public void WhatTheSolverWroteAfterItsLastReadAndToItsOtherStreamIsInTheTranscriptTest()
    {
        var end = new SmtScriptedEnd { ExitCode = 101, Rest = "partial answer\n", Stderr = "(error \"out of memory\")\n" };

        var held = SmtSessionScript.Hold(SmtSolverProblems.Check("check", PROBLEM), new SmtOptionsData(), [SILENT], end);

        Assert.That(held.Outcome.Transcript, Does.EndWith("> (check-sat)\npartial answer\n; the solver's stderr\n(error \"out of memory\")\n"));
    }

    #endregion
}
