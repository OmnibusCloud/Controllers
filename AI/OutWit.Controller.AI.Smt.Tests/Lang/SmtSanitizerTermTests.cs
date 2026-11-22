using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The terms that stand beside a problem: a goal to prove, a term whose value is asked, a
/// constant to project on. One s-expression each, held to the rules of a kept command's atoms.
/// </summary>
[TestFixture]
public class SmtSanitizerTermTests
{
    #region Accept Tests

    [TestCase("x", "x")]
    [TestCase("  |x|  ", "x")]
    [TestCase("|my tree|", "|my tree|")]
    [TestCase("42", "42")]
    [TestCase("#x0F", "#x0F")]
    [TestCase("\"say \"\"hi\"\"\"", "\"say \"\"hi\"\"\"")]
    [TestCase("(> (f x) 0) ; the goal", "(> (f x) 0)")]
    [TestCase("\n(forall ((x Int))\n  (=> (> x 0) (> (* x x) 0)))\n", "(forall ((x Int)) (=> (> x 0) (> (* x x) 0)))")]
    [TestCase("((_ extract 7 0) v)", "((_ extract 7 0) v)")]
    [TestCase("(check-sat)", "(check-sat)")]
    [TestCase("()", "()")]
    public void TermIsOneExpressionWrittenAsTheWriterWritesItTest(string text, string written)
    {
        var result = SmtSanitizer.SanitizeTerm(text);

        Assert.That(result.Refusal, Is.Null);
        Assert.That(result.IsAccepted, Is.True);
        Assert.That(result.Term!.ToString(), Is.EqualTo(written));

        var again = SmtSanitizer.SanitizeTerm(written);
        Assert.That(again.Term!.Is(result.Term), Is.True, "a term written back is the same term");
    }

    [Test]
    public void TermInsideACommandStaysOneExpressionTest()
    {
        // A term is put into a command as the expression it is, never as text: whatever it
        // holds, the command is one command.
        var term = SmtSanitizer.SanitizeTerm("(= s \"x\"\")) (exit) (assert (= t \"\"y\")").Term!;

        var command = SmtScripts.AssertNot(term);
        var read = SmtReader.Read(SmtScripts.Text([command]), SmtReaderLimits.Problem);

        Assert.That(read, Has.Count.EqualTo(1));
        Assert.That(read[0].Is(command), Is.True);
        Assert.That(read[0].Items[1].Items[1].Items[2].Text, Is.EqualTo("x\")) (exit) (assert (= t \"y"));
    }

    #endregion

    #region Refusal Tests

    [TestCase("", 1)]
    [TestCase("  \n ; nothing but a comment", 1)]
    [TestCase("x y", 1)]
    [TestCase("(> x 0)\n(exit)", 2)]
    [TestCase("x) (exit) (assert (", 0)]
    [TestCase("(> x 0)\n\n\"second\"\nthird", 3)]
    public void TextThatIsNotOneExpressionIsRefusedTest(string text, int line)
    {
        var result = SmtSanitizer.SanitizeTerm(text);

        Assert.That(result.Term, Is.Null);
        Assert.That(result.IsAccepted, Is.False);
        SmtSanitizerChecks.HoldRefusal(result.Refusal!);

        // A text that closes a list it did not open is not even SMT-LIB.
        Assert.That(result.Refusal!.Code, Is.EqualTo(line == 0 ? SmtRefusalCodes.SYNTAX_ERROR : SmtRefusalCodes.NOT_A_TERM));
        Assert.That(result.Refusal.Command, Is.Empty);
        if (line > 0)
            Assert.That(result.Refusal.Line, Is.EqualTo(line));
    }

    [TestCase("(> x |let|)", SmtRefusalCodes.RESERVED_SYMBOL)]
    [TestCase("|forall|", SmtRefusalCodes.RESERVED_SYMBOL)]
    [TestCase("(> x -5)", SmtRefusalCodes.AMBIGUOUS_TOKEN)]
    [TestCase("-5", SmtRefusalCodes.AMBIGUOUS_TOKEN)]
    [TestCase("|-5|", SmtRefusalCodes.AMBIGUOUS_TOKEN)]
    [TestCase("(= s \"caf\u00e9\")", SmtRefusalCodes.NON_ASCII_STRING)]
    [TestCase("\"\u00e9\"", SmtRefusalCodes.NON_ASCII_STRING)]
    [TestCase("((_ zero_extend 99999) v)", SmtRefusalCodes.INDEX_TOO_LARGE)]
    [TestCase("(_ bv0 1048576)", SmtRefusalCodes.INDEX_TOO_LARGE)]
    [TestCase("oc!x", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("(> x oc!limit)", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("(let ((oc!y 1)) (> x 0))", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("(forall ((oc!z Int)) true)", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("(! p :named oc!n)", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("|oc!a b|", SmtRefusalCodes.RESERVED_PREFIX)]
    [TestCase("(> x 12abc)", SmtRefusalCodes.SYNTAX_ERROR)]
    [TestCase("(> x 0", SmtRefusalCodes.SYNTAX_ERROR)]
    [TestCase("\"open", SmtRefusalCodes.SYNTAX_ERROR)]
    [TestCase("|a\\|", SmtRefusalCodes.SYNTAX_ERROR)]
    public void TermIsHeldToTheRulesOfAKeptCommandsAtomsTest(string text, string code)
    {
        var result = SmtSanitizer.SanitizeTerm("\n" + text);

        Assert.That(result.Term, Is.Null);
        Assert.That(result.Refusal!.Code, Is.EqualTo(code));
        Assert.That(result.Refusal.Line, Is.EqualTo(2));
        Assert.That(result.Refusal.Command, Is.Empty);
        SmtSanitizerChecks.HoldRefusal(result.Refusal);
    }

    [Test]
    public void TermIsHeldToItsOwnCharacterLimitAndToTheProblemsOtherLimitsTest()
    {
        var atLimit = "(and" + string.Concat(Enumerable.Repeat(" a", (SmtSanitizer.TERM_CHARACTERS - 5) / 2)) + ")";

        Assert.That(atLimit.Length, Is.EqualTo(SmtSanitizer.TERM_CHARACTERS - 1));
        Assert.That(SmtSanitizer.SanitizeTerm(atLimit + " ").IsAccepted, Is.True);
        Assert.That(SmtSanitizer.SanitizeTerm(atLimit + "  ").Refusal!.Code, Is.EqualTo(SmtRefusalCodes.TEXT_TOO_LONG));

        Assert.That(SmtSanitizer.SanitizeTerm(new string('7', 5_000)).Refusal!.Code, Is.EqualTo(SmtRefusalCodes.NUMERAL_TOO_LONG));

        // A term of so few characters cannot nest deeper than a problem may: as deep as its
        // characters let it, it is taken.
        var deepest = SmtSanitizer.TERM_CHARACTERS / 2;
        Assert.That(deepest, Is.LessThan(SmtReaderLimits.PROBLEM_DEPTH));
        Assert.That(SmtSanitizer.SanitizeTerm(new string('(', deepest) + new string(')', deepest)).IsAccepted, Is.True);
    }

    [Test]
    public void TermLongerWhenWrittenThanATermMayBeIsRefusedTest()
    {
        // "(a)(a)" is written "(a) (a)": what would be refused when read back is refused now.
        var dense = "(and" + string.Concat(Enumerable.Repeat("(a)", (SmtSanitizer.TERM_CHARACTERS - 5) / 3)) + ")";

        Assert.That(dense.Length, Is.LessThanOrEqualTo(SmtSanitizer.TERM_CHARACTERS));

        var refusal = SmtSanitizer.SanitizeTerm(dense).Refusal;
        Assert.That(refusal!.Code, Is.EqualTo(SmtRefusalCodes.TEXT_TOO_LONG));
        SmtSanitizerChecks.HoldRefusal(refusal);
    }

    [Test]
    public void RefusalOfATermNeverCarriesTheTextTest()
    {
        var secret = new string('q', 2_500) + new string('z', 2_500);

        foreach (var text in new[] { $"-5{secret}", $"{secret} {secret}", $"({secret}", $"\"{secret}\u00e9\"", $"(_ a 9{new string('9', 4_000)})", $"{secret}'" })
        {
            var refusal = SmtSanitizer.SanitizeTerm(text).Refusal;

            SmtSanitizerChecks.HoldRefusal(refusal!);
            Assert.That(refusal!.Reason, Does.Not.Contain("qz"));
            Assert.That(refusal.Reason, Does.Not.Contain(new string('q', 65)));
            Assert.That(refusal.Reason, Does.Not.Contain(new string('9', 65)));
        }
    }

    #endregion
}
