using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// What a problem holds once it is taken: its commands and its two texts, and what the
/// controller reads off it (the logic, the expected verdict, the attribution, the declarations).
/// </summary>
[TestFixture]
public class SmtProblemTests
{
    #region Constants

    private const string LINEAR =
        "; a small linear problem\n" +
        "(set-info :smt-lib-version 2.6)\n" +
        "(set-logic QF_LIA)\n" +
        "(set-info :status sat)\n" +
        "(declare-const |x| Int)\n" +
        "(declare-fun y () Int)\n" +
        "(assert (and (> x 0)\n" +
        "             (< y 10)\n" +
        "             (= (+ x y) 7)))\n" +
        "(check-sat)\n" +
        "(get-model)\n" +
        "(check-sat)\n" +
        "(exit)\n";

    #endregion

    #region Text Tests

    [Test]
    public void ProblemHoldsTheKeptCommandsAndItsTwoTextsTest()
    {
        var problem = SmtSanitizerChecks.Accept(LINEAR);

        Assert.That(problem.Commands.Select(command => command.Head), Is.EqualTo(new[] { "set-info", "set-logic", "set-info", "declare-const", "declare-fun", "assert" }));
        Assert.That(problem.Commands.Select(command => command.Line), Is.EqualTo(new[] { 2, 3, 4, 5, 6, 7 }));
        Assert.That(problem.SolverCommands.Select(command => command.Head), Is.EqualTo(new[] { "set-logic", "declare-const", "declare-fun", "assert" }));
        Assert.That(problem.Canonical, Is.EqualTo(
            "(set-info :smt-lib-version 2.6)\n(set-logic QF_LIA)\n(set-info :status sat)\n(declare-const x Int)\n(declare-fun y () Int)\n(assert (and (> x 0) (< y 10) (= (+ x y) 7)))\n"));
        Assert.That(problem.SolverText, Is.EqualTo(
            "(set-logic QF_LIA)\n(declare-const x Int)\n(declare-fun y () Int)\n(assert (and (> x 0) (< y 10) (= (+ x y) 7)))\n"));
        Assert.That(problem.Logic, Is.EqualTo("QF_LIA"));
        Assert.That(problem.Status, Is.EqualTo(SmtProblemStatus.Sat));
        Assert.That(problem.AssertionCount, Is.EqualTo(1));
        Assert.That(problem.DeclaredSymbolCount, Is.EqualTo(2));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void ProblemCountsWhatWasStrippedAndRemembersWhereTest()
    {
        var problem = SmtSanitizerChecks.Accept(LINEAR);

        Assert.That(problem.Stripped, Has.Count.EqualTo(3));
        Assert.That(problem.Stripped["check-sat"], Is.EqualTo(2));
        Assert.That(problem.Stripped["get-model"], Is.EqualTo(1));
        Assert.That(problem.Stripped["exit"], Is.EqualTo(1));
        Assert.That(problem.StrippedLines.Select(stripped => (stripped.Line, stripped.Command)),
            Is.EqualTo(new[] { (10, "check-sat"), (11, "get-model"), (12, "check-sat"), (13, "exit") }));
    }

    [Test]
    public void NoSetInfoReachesTheSolverTextTest()
    {
        // The solver acts on some of them: a :status that disagrees with its verdict is an error
        // to it, :smt-lib-version changes how it reads. The canonical text keeps them all.
        var problem = SmtSanitizerChecks.Accept(
            "(set-info :smt-lib-version 2.0)\n(set-info :status unsat)\n(set-info :source |x|)\n(set-info :license \"l\")\n(set-info :anything (else 1))\n(set-info :flag)\n(assert true)\n(set-info :status sat)");

        Assert.That(problem.Commands, Has.Count.EqualTo(8));
        Assert.That(problem.SolverText, Is.EqualTo("(assert true)\n"));
        Assert.That(problem.Canonical, Does.Contain("(set-info :smt-lib-version 2.0)\n"));
        Assert.That(problem.Canonical, Does.Contain("(set-info :status unsat)\n"));
        Assert.That(problem.Status, Is.EqualTo(SmtProblemStatus.Sat));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void ProblemTextIsWrittenByTheWriterOfTheControllersCommandsTest()
    {
        // One writer for the problem and for what the controller puts around it: the two are
        // joined as commands, not as text.
        var problem = SmtSanitizerChecks.Accept("(set-info :status sat)\n(declare-const x Int)\n(assert (> x 0))");

        Assert.That(SmtScripts.Text(problem.Commands), Is.EqualTo(problem.Canonical));
        Assert.That(SmtScripts.Text(problem.SolverCommands), Is.EqualTo(problem.SolverText));
        Assert.That(SmtScripts.Text(problem.SolverCommands.Append(SmtScripts.CheckSat())), Is.EqualTo("(declare-const x Int)\n(assert (> x 0))\n(check-sat)\n"));
    }

    [Test]
    public void TextWithoutCommandsIsAProblemWithoutCommandsTest()
    {
        foreach (var text in new[] { "", " \t\r\n", "; only a comment", "; one\n; two\n\n" })
        {
            var problem = SmtSanitizerChecks.Accept(text);

            Assert.That(problem.Commands, Is.Empty);
            Assert.That(problem.SolverCommands, Is.Empty);
            Assert.That(problem.Canonical, Is.Empty);
            Assert.That(problem.SolverText, Is.Empty);
            Assert.That(problem.Hash, Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
            Assert.That(problem.Logic, Is.Null);
            Assert.That(problem.Status, Is.EqualTo(SmtProblemStatus.None));
            Assert.That(problem.Source, Is.Empty);
            Assert.That(problem.License, Is.Empty);
            Assert.That(problem.DeclaredSymbolCount, Is.Zero);
        }
    }

    [Test]
    public void ProblemCannotBeChangedTest()
    {
        var problem = SmtSanitizerChecks.Accept(LINEAR);

        Assert.Throws<NotSupportedException>(() => ((IList<SmtExpression>)problem.Commands).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SmtExpression>)problem.SolverCommands).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SmtDeclaredConstant>)problem.Constants).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SmtDeclaredFunction>)problem.Functions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SmtStrippedCommand>)problem.StrippedLines).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)problem.Stripped).Clear());
    }

    #endregion

    #region Information Tests

    [TestCase("(set-info :status sat)", SmtProblemStatus.Sat)]
    [TestCase("(set-info :status unsat)", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status |unsat|)", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status unknown)", SmtProblemStatus.None)]
    [TestCase("(set-info :status SAT)", SmtProblemStatus.None)]
    [TestCase("(set-info :status \"sat\")", SmtProblemStatus.None)]
    [TestCase("(set-info :status (sat))", SmtProblemStatus.None)]
    [TestCase("(set-info :status 1)", SmtProblemStatus.None)]
    [TestCase("(set-info :status)", SmtProblemStatus.None)]
    [TestCase("(set-info :Status sat)", SmtProblemStatus.None)]
    [TestCase("(set-info :statuses sat)", SmtProblemStatus.None)]
    [TestCase("(assert (! true :status unsat))", SmtProblemStatus.None)]
    [TestCase("(get-info :status)", SmtProblemStatus.None)]
    [TestCase("(assert true)", SmtProblemStatus.None)]
    [TestCase("(set-info :status sat) (set-info :status unsat)", SmtProblemStatus.Unsat)]
    [TestCase("(set-info :status unsat) (assert true) (set-info :status sat)", SmtProblemStatus.Sat)]
    [TestCase("(set-info :status sat) (set-info :status unknown)", SmtProblemStatus.None)]
    [TestCase("(set-info :status sat) (set-info :status)", SmtProblemStatus.None)]
    public void StatusIsTheLastOneWhenItIsSatOrUnsatTest(string text, SmtProblemStatus status)
    {
        Assert.That(SmtSanitizerChecks.Accept(text).Status, Is.EqualTo(status));
    }

    [Test]
    public void LogicIsTheFirstOneNamedTest()
    {
        Assert.That(SmtSanitizerChecks.Accept("(set-logic |QF_BV|)").Logic, Is.EqualTo("QF_BV"));
        Assert.That(SmtSanitizerChecks.Accept("(set-logic QF_BV)\n(assert true)\n(set-logic ALL)").Logic, Is.EqualTo("QF_BV"));
        Assert.That(SmtSanitizerChecks.Accept("(assert true)").Logic, Is.Null);
    }

    [Test]
    public void SourceAndLicenseAreAStringOrASymbolTest()
    {
        var problem = SmtSanitizerChecks.Accept("(set-info :source |Made by hand.\nTwo lines, a \"quote\".|)\n(set-info :license \"https://creativecommons.org/licenses/by/4.0/\")");

        Assert.That(problem.Source, Is.EqualTo("Made by hand.\nTwo lines, a \"quote\"."));
        Assert.That(problem.License, Is.EqualTo("https://creativecommons.org/licenses/by/4.0/"));
        Assert.That(SmtSanitizerChecks.Accept("(set-info :source \"a \"\"b\"\"\")\n(set-info :license MIT)").Source, Is.EqualTo("a \"b\""));
        Assert.That(SmtSanitizerChecks.Accept("(set-info :source \"s\")\n(set-info :license MIT)").License, Is.EqualTo("MIT"));
    }

    [TestCase("(set-info :source)")]
    [TestCase("(set-info :source 42)")]
    [TestCase("(set-info :source (a b))")]
    [TestCase("(set-info :source :keyword)")]
    [TestCase("(set-info :source \"first\") (set-info :source 42)")]
    [TestCase("(set-info :Source \"other keyword\")")]
    [TestCase("(assert true)")]
    public void SourceOfAnotherKindReadsAsNoneTest(string text)
    {
        Assert.That(SmtSanitizerChecks.Accept(text).Source, Is.Empty);
        Assert.That(SmtSanitizerChecks.Accept(text.Replace(":source", ":license")).License, Is.Empty);
    }

    [Test]
    public void TheLastSourceAndLicenseHoldTest()
    {
        var problem = SmtSanitizerChecks.Accept("(set-info :source \"one\")\n(set-info :license \"l1\")\n(set-info :source two)\n(set-info :license |l 2|)");

        Assert.That(problem.Source, Is.EqualTo("two"));
        Assert.That(problem.License, Is.EqualTo("l 2"));
    }

    [Test]
    public void SourceAndLicenseAreCutToWhatAnAttributionListTakesTest()
    {
        var exact = new string('s', SmtSanitizer.ATTRIBUTION_CHARACTERS);
        const string pair = "\ud83d\ude00";

        Assert.That(SmtSanitizerChecks.Accept($"(set-info :source \"{exact}\")").Source, Is.EqualTo(exact));
        Assert.That(SmtSanitizerChecks.Accept($"(set-info :source \"{exact}tail\")").Source, Is.EqualTo(exact));
        Assert.That(SmtSanitizerChecks.Accept($"(set-info :license |{exact} and a long tail|)").License, Is.EqualTo(exact));

        // A pair that would be cut in half is left out whole.
        var cut = SmtSanitizerChecks.Accept($"(set-info :source |{exact[..^1]}{pair}|)").Source;
        Assert.That(cut, Is.EqualTo(exact[..^1]));

        // The canonical text keeps the whole value: only what feeds the list is cut.
        Assert.That(SmtSanitizerChecks.Accept($"(set-info :source \"{exact}tail\")").Canonical, Does.Contain("stail\""));
    }

    #endregion

    #region Declaration Tests

    [Test]
    public void ConstantsAreTheDeclaredOnesWithTheirSortsTest()
    {
        var problem = SmtSanitizerChecks.Accept(
            "(declare-const a Int)\n" +
            "(declare-fun b () (_ BitVec 8))\n" +
            "(declare-fun f (Int (Array Int Bool)) Bool)\n" +
            "(declare-const |c d| Bool)\n" +
            "(define-fun g () Int 1)\n" +
            "(declare-datatype D ((k)))\n" +
            "(declare-fun h (D) D)\n" +
            "(declare-const |assert| D)\n");

        Assert.That(problem.Constants.Select(constant => constant.Name), Is.EqualTo(new[] { "a", "b", "c d", "assert" }));
        Assert.That(problem.Constants.Select(constant => constant.Symbol.ToString()), Is.EqualTo(new[] { "a", "b", "|c d|", "|assert|" }));
        Assert.That(problem.Constants.Select(constant => constant.Sort.ToString()), Is.EqualTo(new[] { "Int", "(_ BitVec 8)", "Bool", "D" }));

        Assert.That(problem.Functions.Select(function => function.Name), Is.EqualTo(new[] { "f", "h" }));
        Assert.That(problem.Functions[0].Parameters.Select(sort => sort.ToString()), Is.EqualTo(new[] { "Int", "(Array Int Bool)" }));
        Assert.That(problem.Functions[0].Sort.ToString(), Is.EqualTo("Bool"));
        Assert.That(problem.Functions[1].Symbol.ToString(), Is.EqualTo("h"));
        Assert.That(problem.DeclaredSymbolCount, Is.EqualTo(9));
    }

    [Test]
    public void AssertionsAreCountedTest()
    {
        Assert.That(SmtSanitizerChecks.Accept("(assert true)\n(get-model)\n(assert false)\n(assert (assert x))\n(check-sat)").AssertionCount, Is.EqualTo(3));
        Assert.That(SmtSanitizerChecks.Accept("(declare-const x Int)").AssertionCount, Is.Zero);
    }

    #endregion
}
