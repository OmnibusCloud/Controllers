using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// Every refusal code with its line and its command: the reader's faults and limits, what is no
/// command, and the limits the sanitizer counts itself. The rules of single tokens are in
/// <see cref="SmtSanitizerTokenTests"/>.
/// </summary>
[TestFixture]
public class SmtSanitizerRefusalTests
{
    #region Syntax Tests

    [TestCase("(assert true)\n(assert (= x 1)", 2, "a list is not closed")]
    [TestCase("(assert true))", 1, "a closing parenthesis with no list open")]
    [TestCase("(assert\n (= s \"open))", 2, "a string literal is not closed")]
    [TestCase("(declare-const |open Int)", 1, "a quoted symbol is not closed")]
    [TestCase("(declare-const |a\\| Int)", 1, "a backslash in a quoted symbol")]
    [TestCase("(assert (> x 007))", 1, "a numeral with a leading zero")]
    [TestCase("\n\n(assert (> x 12abc))", 3, "a token that starts with a digit is a numeral, or a decimal with digits on both sides of one dot")]
    [TestCase("(assert #xZZ)", 1, "a literal that starts with # is #x and hexadecimal digits, or #b and binary digits")]
    [TestCase("(set-info : x)", 1, "a keyword is a colon and a simple symbol")]
    [TestCase("(assert a'b)", 1, "the character ''' cannot stand in a simple symbol")]
    public void TextThatIsNotSmtLibIsASyntaxErrorTest(string text, int line, string reason)
    {
        var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.SYNTAX_ERROR);

        Assert.That(refusal.Line, Is.EqualTo(line));
        Assert.That(refusal.Command, Is.Empty);
        Assert.That(refusal.Reason, Is.EqualTo(reason));
    }

    [Test]
    public void CharactersOutsideTheLexiconAreASyntaxErrorTest()
    {
        // Built here: these characters do not survive a test's name.
        var texts = new[]
        {
            "(assert\0 true)",
            "(assert true)\f(assert true)",
            "(assert true)\v",
            "(assert " + (char)27 + "[0m true)",
            "(assert (= s \"a\0b\"))",
            "(declare-const |a" + (char)127 + "| Int)",
            "\ufeff(assert true)",
            "(assert\u00a0true)",
            "(assert\u2028true)",
            "(assert\u3000true)",
            "(declare-const caf\u00e9 Int)",
            "(assert (= s \"\ud83d\"))"
        };

        foreach (var text in texts)
        {
            var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.SYNTAX_ERROR);

            Assert.That(refusal.Line, Is.EqualTo(1), Show(text));
            Assert.That(refusal.Command, Is.Empty, Show(text));
        }
    }

    [Test]
    public void CarriageReturnThatEndsNoLineOfACommentIsASyntaxErrorTest()
    {
        // One reader ends the comment at the carriage return and finds a command after it,
        // another reads the command as comment: no problem is made of such a text.
        var cases = new (string Text, int Line)[]
        {
            ("; a note\r(set-option :produce-proofs true)\n(assert true)", 1),
            ("(declare-const p Bool)\n(assert p) ; a note\r(assert (not p))\n", 2),
            ("(assert true)\n; the end\r", 2)
        };

        foreach (var (text, line) in cases)
        {
            var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.SYNTAX_ERROR);

            Assert.That(refusal.Line, Is.EqualTo(line), Show(text));
            Assert.That(refusal.Command, Is.Empty);
            Assert.That(refusal.Reason, Is.EqualTo("a carriage return in a comment with no line feed after it"));
        }

        var problem = SmtSanitizerChecks.Accept("; a note\r\n(declare-const p Bool) ; another\r\n(assert p)\r(assert (not p))\r");
        Assert.That(problem.Canonical, Is.EqualTo("(declare-const p Bool)\n(assert p)\n(assert (not p))\n"));
    }

    [Test]
    public void SyntaxErrorAnywhereComesBeforeEveryOtherRefusalTest()
    {
        // The whole text is read before a command is looked at: a text that is not SMT-LIB is
        // refused as such, whatever commands it starts with.
        var refusal = SmtSanitizerChecks.Refuse("(push 1)\n(set-option :a 1)\n(assert (= x 1)", SmtRefusalCodes.SYNTAX_ERROR);

        Assert.That(refusal.Line, Is.EqualTo(3));
    }

    #endregion

    #region Limit Tests

    [Test]
    public void TextPastTheCharacterLimitIsRefusedTest()
    {
        var atLimit = "(assert true)" + new string(' ', SmtReaderLimits.PROBLEM_CHARACTERS - 13);

        Assert.That(SmtSanitizerChecks.Accept(atLimit).Canonical, Is.EqualTo("(assert true)\n"));

        var refusal = SmtSanitizerChecks.Refuse(atLimit + " ", SmtRefusalCodes.TEXT_TOO_LONG);
        Assert.That(refusal.Line, Is.EqualTo(1));
        Assert.That(refusal.Command, Is.Empty);
    }

    [Test]
    public void TextPastTheTokenLimitIsRefusedTest()
    {
        // "(assert (and" is four tokens, "))" two more: the atoms between them fill the limit.
        var atoms = SmtReaderLimits.PROBLEM_TOKENS - 6;
        var atLimit = Repeated("(assert (and", " a", atoms, "))");
        var past = Repeated("(assert (and", " a", atoms + 1, "))");

        Assert.That(SmtSanitizerChecks.Accept(atLimit).AssertionCount, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse(past, SmtRefusalCodes.TOO_MANY_TOKENS).Command, Is.Empty);
    }

    [Test]
    public void NestingPastTheDepthLimitIsRefusedTest()
    {
        // The command is the first level, its term every other level of the limit.
        var deepest = "(assert " + new string('(', SmtReaderLimits.PROBLEM_DEPTH - 1) + new string(')', SmtReaderLimits.PROBLEM_DEPTH - 1) + ")";
        var deeper = "(assert " + new string('(', SmtReaderLimits.PROBLEM_DEPTH) + new string(')', SmtReaderLimits.PROBLEM_DEPTH) + ")";

        SmtSanitizerChecks.HoldProperties(SmtSanitizerChecks.Accept(deepest));
        Assert.That(SmtSanitizerChecks.Refuse("\n" + deeper, SmtRefusalCodes.NESTING_TOO_DEEP).Line, Is.EqualTo(2));
    }

    [Test]
    public void NumeralPastTheDigitLimitIsRefusedTest()
    {
        var longest = new string('7', SmtReaderLimits.PROBLEM_NUMERAL_DIGITS);

        SmtSanitizerChecks.Accept($"(assert (> x {longest}))");
        SmtSanitizerChecks.Accept($"(assert (> x {longest[..^1]}.5))");

        var refusal = SmtSanitizerChecks.Refuse($"(assert\n (> x {new string('7', 5_000)}))", SmtRefusalCodes.NUMERAL_TOO_LONG);
        Assert.That(refusal.Line, Is.EqualTo(2));
        Assert.That(refusal.Reason, Does.Not.Contain("77"));

        SmtSanitizerChecks.Refuse($"(assert (> x {longest}.5))", SmtRefusalCodes.NUMERAL_TOO_LONG);
    }

    [Test]
    public void LiteralPastTheWidthLimitIsRefusedTest()
    {
        SmtSanitizerChecks.Accept($"(assert (= x #b{new string('1', SmtReaderLimits.PROBLEM_LITERAL_BITS)}))");
        SmtSanitizerChecks.Accept($"(assert (= x #x{new string('f', SmtReaderLimits.PROBLEM_LITERAL_BITS / 4)}))");

        var refusal = SmtSanitizerChecks.Refuse($"(assert (= x #b{new string('1', 70_000)}))", SmtRefusalCodes.LITERAL_TOO_WIDE);
        Assert.That(refusal.Reason, Does.Not.Contain("11"));

        SmtSanitizerChecks.Refuse($"(assert (= x #x{new string('f', SmtReaderLimits.PROBLEM_LITERAL_BITS / 4 + 1)}))", SmtRefusalCodes.LITERAL_TOO_WIDE);
    }

    [Test]
    public void StringPastTheLengthLimitIsRefusedTest()
    {
        SmtSanitizerChecks.Accept($"(assert (= s \"{new string('a', SmtReaderLimits.PROBLEM_STRING_CHARACTERS)}\"))");

        var refusal = SmtSanitizerChecks.Refuse($"(assert (= s \"{new string('a', SmtReaderLimits.PROBLEM_STRING_CHARACTERS + 1)}\"))", SmtRefusalCodes.STRING_TOO_LONG);
        Assert.That(refusal.Reason, Does.Not.Contain("aa"));
    }

    [Test]
    public void ProblemLongerWhenWrittenThanAProblemMayBeIsRefusedTest()
    {
        // "(aaaaaaaaa)(aaaaaaaaa)" is written "(aaaaaaaaa) (aaaaaaaaa)": a text under the limit
        // whose own canonical text would be past it is refused, so that the canonical text of
        // every problem reads again.
        const string item = "(aaaaaaaaa)";
        var count = (SmtReaderLimits.PROBLEM_CHARACTERS - 20) / item.Length;
        var text = Repeated("(assert (and", item, count, "))");

        Assert.That(text.Length, Is.LessThanOrEqualTo(SmtReaderLimits.PROBLEM_CHARACTERS));
        Assert.That(text.Length + count, Is.GreaterThan(SmtReaderLimits.PROBLEM_CHARACTERS));

        var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.TEXT_TOO_LONG);
        Assert.That(refusal.Reason, Does.StartWith("written with one blank between two tokens the text has "));

        SmtSanitizerChecks.HoldProperties(SmtSanitizerChecks.Accept(Repeated("(assert (and", item, count / 2, "))")));
    }

    [Test]
    public void MoreDeclaredNamesThanAllowedAreRefusedTest()
    {
        var atLimit = Declarations(SmtSanitizer.MAX_DECLARED_SYMBOLS);

        var problem = SmtSanitizerChecks.Accept(atLimit);
        Assert.That(problem.DeclaredSymbolCount, Is.EqualTo(100_000));
        Assert.That(problem.Constants, Has.Count.EqualTo(100_000));

        var refusal = SmtSanitizerChecks.Refuse(atLimit + "(declare-const last Bool)\n", SmtRefusalCodes.TOO_MANY_SYMBOLS);
        Assert.That(refusal.Line, Is.EqualTo(100_001));
        Assert.That(refusal.Command, Is.EqualTo("declare-const"));
    }

    [Test]
    public void NamesOfOneDatatypeCountTowardsTheLimitTest()
    {
        // One command, 100,001 names: the datatype and its constructors.
        var constructors = new StringBuilder();
        for (var index = 0; index < SmtSanitizer.MAX_DECLARED_SYMBOLS - 1; index++)
            constructors.Append("(c").Append(index).Append(')');

        var atLimit = $"(declare-datatype D ({constructors}))";
        var past = $"(declare-datatype D ({constructors}(one-more)))";

        Assert.That(SmtSanitizerChecks.Accept(atLimit).DeclaredSymbolCount, Is.EqualTo(100_000));
        Assert.That(SmtSanitizerChecks.Refuse(past, SmtRefusalCodes.TOO_MANY_SYMBOLS).Command, Is.EqualTo("declare-datatype"));
    }

    #endregion

    #region Command Tests

    [Test]
    public void TopLevelFormThatIsNoCommandIsRefusedTest()
    {
        var cases = new (string Text, string Reason)[]
        {
            ("x", "this is an atom"),
            ("assert", "this is an atom"),
            ("42", "this is an atom"),
            ("\"(check-sat)\"", "this is an atom"),
            (":status", "this is an atom"),
            ("#b01", "this is an atom"),
            ("|assert|", "this is an atom"),
            ("()", "this list is empty"),
            ("((assert true))", "this list starts with something else"),
            ("(\"assert\" true)", "this list starts with something else"),
            ("(:status sat)", "this list starts with something else"),
            ("(5 x)", "this list starts with something else"),
            ("(() assert true)", "this list starts with something else")
        };

        foreach (var (text, reason) in cases)
        {
            var refusal = SmtSanitizerChecks.Refuse($"(declare-const x Bool)\n\n{text}\n(assert x)", SmtRefusalCodes.NOT_A_COMMAND);

            Assert.That(refusal.Line, Is.EqualTo(3), text);
            Assert.That(refusal.Command, Is.Empty, text);
            Assert.That(refusal.Reason, Is.EqualTo("a command is a list that starts with its name; " + reason), text);
        }
    }

    [Test]
    public void TheFirstFaultOfTheTextIsTheOneRefusedTest()
    {
        Assert.That(SmtSanitizerChecks.Refuse("(assert)\n(push 1)", SmtRefusalCodes.COMMAND_MALFORMED).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(push 1)\n(assert)", SmtRefusalCodes.COMMAND_NOT_ALLOWED).Line, Is.EqualTo(1));
        Assert.That(SmtSanitizerChecks.Refuse("(assert true)\nx\n(assert)", SmtRefusalCodes.NOT_A_COMMAND).Line, Is.EqualTo(2));
        Assert.That(SmtSanitizerChecks.Refuse("(check-sat)\n(declare-const oc!a Int)\n()", SmtRefusalCodes.RESERVED_PREFIX).Line, Is.EqualTo(2));
    }

    [Test]
    public void SanitizeRefusesNoTextAtAllByAnExceptionTest()
    {
        Assert.Throws<ArgumentNullException>(() => SmtSanitizer.Sanitize(null!));
        Assert.Throws<ArgumentNullException>(() => SmtSanitizer.SanitizeTerm(null!));
    }

    #endregion

    #region Tools

    private static string Repeated(string start, string item, int count, string end)
    {
        var builder = new StringBuilder(start.Length + item.Length * count + end.Length);
        builder.Append(start);
        for (var index = 0; index < count; index++)
            builder.Append(item);

        return builder.Append(end).ToString();
    }

    private static string Declarations(int count)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < count; index++)
            builder.Append("(declare-const b").Append(index).Append(" Bool)\n");

        return builder.ToString();
    }

    private static string Show(string text)
    {
        return string.Join(' ', text.Select(character => ((int)character).ToString("X4")));
    }

    #endregion
}
