using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// Where a problem ends: at its first <c>check-sat</c> and at its <c>exit</c>. After
/// <c>exit</c> a solver runs nothing; a declaration or an assertion after a <c>check-sat</c>
/// makes the script an incremental one, whose first answer is for less than the whole text. A
/// command that states the problem is refused there; a question to the solver is stripped
/// wherever it stands.
/// </summary>
[TestFixture]
public class SmtSanitizerEndTests
{
    #region Constants

    private const string REASON_AFTER_EXIT = "the command stands after exit: a solver runs nothing after it";
    private const string REASON_AFTER_CHECK = "the command stands after check-sat: a script that goes on after a check is incremental, and its first answer is for less than the whole text";

    /// <summary>Every kept command, in a shape the standard gives it.</summary>
    private static readonly string[] KEPT =
    [
        "(set-logic QF_LIA)",
        "(set-info :status sat)",
        "(declare-sort S 0)",
        "(define-sort T () Int)",
        "(declare-const x Int)",
        "(declare-fun f (Int) Int)",
        "(define-fun g ((x Int)) Int x)",
        "(define-fun-rec h ((x Int)) Int (h x))",
        "(define-funs-rec ((k ((x Int)) Int)) ((k x)))",
        "(declare-datatype D ((mk (get Int))))",
        "(declare-datatypes ((E 0)) (((mk2 (get2 Int)))))",
        "(assert true)"
    ];

    #endregion

    #region Refusal Tests

    [TestCaseSource(nameof(Kept))]
    public void KeptCommandAfterCheckSatRefusesTheProblemTest(string command)
    {
        var head = SmtReader.Read(command, SmtReaderLimits.Problem).Single().Head;

        foreach (var (text, line) in new[] { ($"(check-sat)\n{command}", 2), ($"(declare-const before Bool)\n(assert before)\n(check-sat)\n(get-model)\n\n{command}\n(check-sat)", 6) })
        {
            var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.COMMAND_AFTER_END);

            Assert.That(refusal.Command, Is.EqualTo(head));
            Assert.That(refusal.Line, Is.EqualTo(line));
            Assert.That(refusal.Reason, Is.EqualTo(REASON_AFTER_CHECK));
        }
    }

    [TestCaseSource(nameof(Kept))]
    public void KeptCommandAfterExitRefusesTheProblemTest(string command)
    {
        var head = SmtReader.Read(command, SmtReaderLimits.Problem).Single().Head;

        foreach (var (text, line) in new[] { ($"(exit)\n{command}", 2), ($"(declare-const before Bool)\n(assert before)\n(check-sat)\n(exit)\n{command}", 5), ($"(exit)\n(check-sat)\n{command}", 3) })
        {
            var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.COMMAND_AFTER_END);

            Assert.That(refusal.Command, Is.EqualTo(head));
            Assert.That(refusal.Line, Is.EqualTo(line));
            Assert.That(refusal.Reason, Is.EqualTo(REASON_AFTER_EXIT));
        }
    }

    [Test]
    public void TheEndIsLookedAtBeforeWhatTheLateCommandHoldsTest()
    {
        // The command's place is wrong whatever it holds: its tokens and its shape are not the news.
        Assert.That(SmtSanitizerChecks.Refuse("(check-sat)\n(assert |let|)", SmtRefusalCodes.COMMAND_AFTER_END).Command, Is.EqualTo("assert"));
        Assert.That(SmtSanitizerChecks.Refuse("(exit)\n(declare-const)", SmtRefusalCodes.COMMAND_AFTER_END).Command, Is.EqualTo("declare-const"));
        Assert.That(SmtSanitizerChecks.Refuse("(exit)\n(declare-const oc!x Int)", SmtRefusalCodes.COMMAND_AFTER_END).Command, Is.EqualTo("declare-const"));
    }

    [Test]
    public void WhatIsNoKeptCommandIsRefusedForWhatItIsAfterTheEndTooTest()
    {
        Assert.That(SmtSanitizerChecks.Refuse("(check-sat)\n(push 1)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Command, Is.EqualTo("push"));
        Assert.That(SmtSanitizerChecks.Refuse("(exit)\n(frobnicate)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Command, Is.EqualTo("frobnicate"));
        Assert.That(SmtSanitizerChecks.Refuse("(exit)\n(|assert| true)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Command, Is.EqualTo("assert"));
        Assert.That(SmtSanitizerChecks.Refuse("(exit)\nassert", SmtRefusalCodes.NOT_A_COMMAND).Line, Is.EqualTo(2));
    }

    #endregion

    #region Accept Tests

    [Test]
    public void StrippedCommandsMayFollowTheEndTest()
    {
        var problem = SmtSanitizerChecks.Accept(
            "(declare-const p Bool)\n(assert p)\n(check-sat)\n(get-model)\n(get-value (p))\n(get-info :reason-unknown)\n(check-sat)\n(exit)\n(get-model)\n(check-sat)\n(exit)\n");

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const p Bool)\n(assert p)\n"));
        Assert.That(problem.Stripped["check-sat"], Is.EqualTo(3));
        Assert.That(problem.Stripped["exit"], Is.EqualTo(2));
        Assert.That(problem.Stripped["get-model"], Is.EqualTo(2));
        Assert.That(problem.Stripped["get-value"], Is.EqualTo(1));
        Assert.That(problem.Stripped["get-info"], Is.EqualTo(1));
        Assert.That(problem.StrippedLines.Select(stripped => stripped.Line), Is.EqualTo(new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11 }));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [TestCase("get-model")]
    [TestCase("get-value")]
    [TestCase("get-info")]
    public void QuestionBetweenTheCommandsDoesNotEndTheProblemTest(string name)
    {
        // Only a check and an exit end a problem: a question about a model or about the solver
        // changes nothing of what the commands after it state.
        var problem = SmtSanitizerChecks.Accept($"({name})\n(declare-const x Bool)\n({name} (x))\n(assert x)\n({name} :all)\n(set-info :status sat)\n");

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const x Bool)\n(assert x)\n(set-info :status sat)\n"));
        Assert.That(problem.Stripped[name], Is.EqualTo(3));
        Assert.That(problem.StrippedLines.Select(stripped => (stripped.Line, stripped.Command)), Is.EqualTo(new[] { (1, name), (3, name), (5, name) }));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void ProblemWithoutACheckOrAnExitHasNoEndTest()
    {
        var problem = SmtSanitizerChecks.Accept("(declare-const x Bool)\n(assert x)\n(assert (not x))\n(set-info :status unsat)");

        Assert.That(problem.Commands, Has.Count.EqualTo(4));
        Assert.That(problem.Stripped, Is.Empty);
    }

    [Test]
    public void CheckSatAndExitInsideATermOrAStringEndNothingTest()
    {
        var problem = SmtSanitizerChecks.Accept("(assert (check-sat))\n(assert (= s \"(exit)\"))\n(declare-const |(exit)| Bool)\n(set-info :note (exit))\n(assert exit)");

        Assert.That(problem.Commands, Has.Count.EqualTo(5));
        Assert.That(problem.Stripped, Is.Empty);
    }

    #endregion

    #region Tools

    private static IEnumerable<string> Kept()
    {
        return KEPT;
    }

    #endregion
}
