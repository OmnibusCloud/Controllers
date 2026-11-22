using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

[TestFixture]
public class SmtReaderTests
{
    #region Atom Tests

    [Test]
    public void ReadClassifiesEveryAtomKindTest()
    {
        var items = ReadOne("(x :status 42 0 1.50 #x0F #b0101 \"text\" |a b|)").Items;

        Assert.That(items.Select(item => item.Kind), Is.EqualTo(new[]
        {
            SmtExpressionKind.Symbol, SmtExpressionKind.Keyword, SmtExpressionKind.Numeral, SmtExpressionKind.Numeral,
            SmtExpressionKind.Decimal, SmtExpressionKind.Hexadecimal, SmtExpressionKind.Binary, SmtExpressionKind.String,
            SmtExpressionKind.Symbol
        }));
        Assert.That(items.Select(item => item.Text), Is.EqualTo(new[] { "x", ":status", "42", "0", "1.50", "#x0F", "#b0101", "text", "a b" }));
        Assert.That(items[8].IsQuoted, Is.True);
        Assert.That(items[0].IsQuoted, Is.False);
    }

    [Test]
    public void ReadTakesEveryMarkOfASimpleSymbolTest()
    {
        const string symbol = "~!@$%^&*_-+=<>.?/aZ09";

        Assert.That(ReadOne(symbol).Text, Is.EqualTo(symbol));
        Assert.That(ReadOne(symbol).Kind, Is.EqualTo(SmtExpressionKind.Symbol));
    }

    [Test]
    public void ReadTakesAMinusBeforeDigitsAsASymbolTest()
    {
        // The standard has no negative numeral: -5 is a simple symbol. A consumer that hands
        // text to a solver which reads it as a number refuses it by a rule of its own.
        Assert.That(ReadOne("-5").Kind, Is.EqualTo(SmtExpressionKind.Symbol));
    }

    [Test]
    public void ReadUnescapesDoubledQuotesInAStringTest()
    {
        Assert.That(ReadOne("\"say \"\"hi\"\"\"").Text, Is.EqualTo("say \"hi\""));
        Assert.That(ReadOne("\"\"").Text, Is.EqualTo(""));
    }

    [Test]
    public void ReadKeepsBackslashesOfAStringAsTheyAreTest()
    {
        Assert.That(ReadOne("\"a\\u{5c}\\n\"").Text, Is.EqualTo("a\\u{5c}\\n"));
    }

    [Test]
    public void ReadKeepsLineBreaksAndOtherCharactersOfAQuotedSymbolTest()
    {
        var symbol = ReadOne("|a\n(b) \"c\" ; d \u00e9|");

        Assert.That(symbol.Text, Is.EqualTo("a\n(b) \"c\" ; d \u00e9"));
        Assert.That(symbol.IsQuoted, Is.True);
    }

    [Test]
    public void ReadTakesAnEmptyQuotedSymbolTest()
    {
        Assert.That(ReadOne("||").Text, Is.EqualTo(""));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void ReadReturnsTopLevelExpressionsInOrderTest()
    {
        var expressions = SmtReader.Read("(set-logic QF_LIA)\n(declare-const x Int)\n(assert (> x 0))\n", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.Head), Is.EqualTo(new[] { "set-logic", "declare-const", "assert" }));
        Assert.That(expressions[2].Items[1].Items.Select(item => item.Text), Is.EqualTo(new[] { ">", "x", "0" }));
    }

    [Test]
    public void ReadReturnsNothingForBlanksAndCommentsTest()
    {
        Assert.That(SmtReader.Read("", SmtReaderLimits.Problem), Is.Empty);
        Assert.That(SmtReader.Read("  \t\r\n ; only a comment", SmtReaderLimits.Problem), Is.Empty);
    }

    [Test]
    public void ReadDropsACommentWhateverItHoldsTest()
    {
        var expressions = SmtReader.Read("a ; (b \"c |d \0 \u00e9\nb", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.Text), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void ReadRefusesACarriageReturnInACommentThatNoLineFeedFollowsTest()
    {
        // The standard ends a comment at a carriage return, a solver that ends it at the line
        // feed only reads the rest of the line as comment: the text is refused, at the carriage
        // return, instead of losing a command for one of the two. At the start of a text, in
        // the middle and at its very end.
        var cases = new (string Text, int Line, int Column)[]
        {
            ("; x\r(assert false)", 1, 4),
            (";\r", 1, 2),
            ("a ; x\r(assert false)", 1, 6),
            ("a\n(b) ; note\rc\n", 2, 11),
            ("; x\r\n; y\rz\n", 2, 4),
            ("a ; x\r\r\n", 1, 6),
            ("a ; end\r", 1, 8),
            ("a\n; the last line\r", 2, 16)
        };

        foreach (var (text, line, column) in cases)
        {
            var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read(text, SmtReaderLimits.Problem), Show(text))!;

            Assert.That(refusal.Fault, Is.EqualTo(SmtSyntaxFault.BadCharacter), Show(text));
            Assert.That((refusal.Line, refusal.Column), Is.EqualTo((line, column)), Show(text));
            Assert.That(refusal.Reason, Is.EqualTo("a carriage return in a comment with no line feed after it"));
        }
    }

    [Test]
    public void ReadEndsACommentAtACarriageReturnWithItsLineFeedTest()
    {
        var expressions = SmtReader.Read("; first\r\na ; x (hidden)\r\nb ; y\r\n; last\r\n", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.Text), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(expressions.Select(expression => expression.Line), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(SmtReader.Read("; only a comment\r\n", SmtReaderLimits.Problem), Is.Empty);
    }

    [Test]
    public void ReadTakesALoneCarriageReturnOutsideACommentAsABlankTest()
    {
        var expressions = SmtReader.Read("a\r(b\rc)\r", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.ToString()), Is.EqualTo(new[] { "a", "(b c)" }));
        Assert.That(SmtReader.Read("\"a\rb\" |c\rd|", SmtReaderLimits.Problem).Select(expression => expression.Text), Is.EqualTo(new[] { "a\rb", "c\rd" }));
    }

    [Test]
    public void ReadSeparatesTokensAtEveryDelimiterTest()
    {
        var expressions = SmtReader.Read("a\"b\"c|d|(e)f;g\nh", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.ToString()), Is.EqualTo(new[] { "a", "\"b\"", "c", "d", "(e)", "f", "h" }));
    }

    [Test]
    public void ReadRecordsTheLineEveryExpressionStartsOnTest()
    {
        var expressions = SmtReader.Read("a\n\n(b\n c)\n\"x\ny\" |p\nq| z", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.Line), Is.EqualTo(new[] { 1, 3, 5, 6, 7 }));
        Assert.That(expressions[1].Items[1].Line, Is.EqualTo(4));
    }

    [Test]
    public void ReadTakesNestingFarDeeperThanACallStackTest()
    {
        const int depth = 200_000;
        var text = new string('(', depth) + "x" + new string(')', depth);

        var expression = SmtReader.Read(text, SmtReaderLimits.Unbounded).Single();

        var levels = 0;
        while (expression.IsList)
        {
            levels++;
            expression = expression.Items[0];
        }

        Assert.That(levels, Is.EqualTo(depth));
        Assert.That(expression.Text, Is.EqualTo("x"));
    }

    #endregion

    #region Refusal Tests

    [TestCase("(a", SmtSyntaxFault.UnbalancedParenthesis, 1, 1)]
    [TestCase("a\n  (b (c)", SmtSyntaxFault.UnbalancedParenthesis, 2, 3)]
    [TestCase("a)", SmtSyntaxFault.UnbalancedParenthesis, 1, 2)]
    [TestCase("\"open", SmtSyntaxFault.UnclosedLiteral, 1, 1)]
    [TestCase("x \"a\"\"", SmtSyntaxFault.UnclosedLiteral, 1, 3)]
    [TestCase("|open", SmtSyntaxFault.UnclosedLiteral, 1, 1)]
    [TestCase("|a\\|b|", SmtSyntaxFault.BadCharacter, 1, 3)]
    [TestCase("caf\u00e9", SmtSyntaxFault.BadCharacter, 1, 4)]
    [TestCase("a'b", SmtSyntaxFault.BadCharacter, 1, 1)]
    [TestCase("a,b", SmtSyntaxFault.BadCharacter, 1, 1)]
    [TestCase("a:b", SmtSyntaxFault.BadCharacter, 1, 1)]
    [TestCase("[x]", SmtSyntaxFault.BadCharacter, 1, 1)]
    [TestCase("a\\b", SmtSyntaxFault.BadCharacter, 1, 1)]
    [TestCase("12abc", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("007", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("00", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("01.5", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("1.", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("1.2.3", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("1.5e3", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("1?", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#x", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#b", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#xG1", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#b012", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#X0F", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("#d10", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase(":", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase(":1a", SmtSyntaxFault.MalformedToken, 1, 1)]
    [TestCase("(a\n  :b:c)", SmtSyntaxFault.MalformedToken, 2, 3)]
    public void ReadRefusesWhatIsNotInTheLexiconTest(string text, SmtSyntaxFault fault, int line, int column)
    {
        var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read(text, SmtReaderLimits.Problem))!;

        Assert.That(refusal.Fault, Is.EqualTo(fault));
        Assert.That((refusal.Line, refusal.Column), Is.EqualTo((line, column)));
        Assert.That(refusal.Message, Does.StartWith($"line {line}, column {column}: "));
    }

    [Test]
    public void ReadRefusesControlCharactersAndOtherBlanksTest()
    {
        // Built here, not in attributes: these characters do not survive a test's name.
        var cases = new (string Text, int Column)[]
        {
            ("a\fb", 2),            // a form feed is no blank
            ("a\vb", 2),            // nor a vertical tab
            ("\0", 1),
            ("a\u00a0b", 2),        // a no-break space
            ("\u2028", 1),          // a line separator
            ("\ufeff(a)", 1),       // a byte order mark
            ("\"a\0b\"", 3),
            ("\"a\u001bb\"", 3),
            ("|a\u007fb|", 3),
            ("|a\0b|", 3)
        };

        foreach (var (text, column) in cases)
        {
            var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read(text, SmtReaderLimits.Problem), Show(text))!;

            Assert.That(refusal.Fault, Is.EqualTo(SmtSyntaxFault.BadCharacter), Show(text));
            Assert.That((refusal.Line, refusal.Column), Is.EqualTo((1, column)), Show(text));
        }
    }

    [Test]
    public void ReadRefusesHalfOfASurrogatePairTest()
    {
        const char high = '\ud83d';
        const char low = '\ude00';

        foreach (var text in new[] { $"\"{high}\"", $"\"{low}\"", $"|{high}|", $"|{low}|", $"\"{low}{high}\"", $"\"{high}", $"|{high}" })
        {
            var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read(text, SmtReaderLimits.Problem), Show(text))!;

            Assert.That(refusal.Fault, Is.EqualTo(SmtSyntaxFault.BadCharacter), Show(text));
            Assert.That((refusal.Line, refusal.Column), Is.EqualTo((1, 2)), Show(text));
        }
    }

    [Test]
    public void ReadTakesTabsAndLineBreaksInAStringTest()
    {
        Assert.That(ReadOne("\"a\tb\r\nc\"").Text, Is.EqualTo("a\tb\r\nc"));
    }

    [Test]
    public void ReadTakesAWholeSurrogatePairInAStringAndInAQuotedSymbolTest()
    {
        const string pair = "\ud83d\ude00";

        Assert.That(ReadOne($"\"{pair}\"").Text, Is.EqualTo(pair));
        Assert.That(ReadOne($"|{pair}|").Text, Is.EqualTo(pair));
    }

    [Test]
    public void ReadRefusalNeverQuotesTheTextTest()
    {
        var secret = new string('s', 5_000);

        var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read($"(assert {secret}'x)", SmtReaderLimits.Problem))!;

        Assert.That(refusal.Message, Does.Not.Contain("sss"));
    }

    #endregion

    #region Limit Tests

    [Test]
    public void ReadRefusesATextPastTheCharacterLimitTest()
    {
        var limits = new SmtReaderLimits { MaxCharacters = 10 };

        Assert.That(SmtReader.Read("(a b c d )", limits), Has.Count.EqualTo(1));
        AssertRefused("(a b c d  )", limits, SmtSyntaxFault.TextTooLong);
    }

    [Test]
    public void ReadCountsEveryAtomAndParenthesisAsATokenTest()
    {
        var limits = new SmtReaderLimits { MaxTokens = 5 };

        Assert.That(SmtReader.Read("(a b c)", limits), Has.Count.EqualTo(1));
        AssertRefused("(a b c) d", limits, SmtSyntaxFault.TooManyTokens);
        AssertRefused("(a b c d)", limits, SmtSyntaxFault.TooManyTokens);
    }

    [Test]
    public void ReadRefusesNestingPastTheDepthLimitTest()
    {
        var limits = new SmtReaderLimits { MaxDepth = 3 };

        Assert.That(SmtReader.Read("(((a))) (((b)))", limits), Has.Count.EqualTo(2));
        AssertRefused("((((a))))", limits, SmtSyntaxFault.NestingTooDeep);
    }

    [Test]
    public void ReadRefusesANumberPastTheDigitLimitTest()
    {
        var limits = new SmtReaderLimits { MaxNumeralDigits = 4 };

        Assert.That(SmtReader.Read("1234 12.34", limits), Has.Count.EqualTo(2));
        AssertRefused("12345", limits, SmtSyntaxFault.NumeralTooLong);
        AssertRefused("123.45", limits, SmtSyntaxFault.NumeralTooLong);
    }

    [Test]
    public void ReadRefusesALiteralPastTheBitLimitTest()
    {
        var limits = new SmtReaderLimits { MaxLiteralBits = 8 };

        Assert.That(SmtReader.Read("#xFF #b10101010", limits), Has.Count.EqualTo(2));
        AssertRefused("#xFFF", limits, SmtSyntaxFault.LiteralTooWide);
        AssertRefused("#b101010101", limits, SmtSyntaxFault.LiteralTooWide);
    }

    [Test]
    public void ReadRefusesAStringPastTheLengthLimitTest()
    {
        var limits = new SmtReaderLimits { MaxStringCharacters = 3 };

        Assert.That(ReadOne("\"a\"\"c\"", limits).Text, Is.EqualTo("a\"c"));
        AssertRefused("\"abcd\"", limits, SmtSyntaxFault.StringTooLong);
    }

    [Test]
    public void ReadHoldsAProblemToItsCeilingsTest()
    {
        Assert.That(SmtReaderLimits.Problem.MaxCharacters, Is.EqualTo(4 * 1024 * 1024));
        Assert.That(SmtReaderLimits.Problem.MaxTokens, Is.EqualTo(2_000_000));
        Assert.That(SmtReaderLimits.Problem.MaxDepth, Is.EqualTo(100_000));
        Assert.That(SmtReaderLimits.Problem.MaxNumeralDigits, Is.EqualTo(4_096));
        Assert.That(SmtReaderLimits.Problem.MaxLiteralBits, Is.EqualTo(65_536));
        Assert.That(SmtReaderLimits.Problem.MaxStringCharacters, Is.EqualTo(65_536));

        var deepest = new string('(', 100_000) + new string(')', 100_000);
        Assert.That(SmtReader.Read(deepest, SmtReaderLimits.Problem), Has.Count.EqualTo(1));
        AssertRefused("(" + deepest + ")", SmtReaderLimits.Problem, SmtSyntaxFault.NestingTooDeep);

        Assert.That(SmtReader.Read("#b" + new string('1', 65_536), SmtReaderLimits.Problem), Has.Count.EqualTo(1));
        AssertRefused("#x" + new string('f', 16_385), SmtReaderLimits.Problem, SmtSyntaxFault.LiteralTooWide);
    }

    #endregion

    #region Robustness Tests

    [Test]
    public void ReadEndsEveryRandomTextWithExpressionsOrARefusalTest()
    {
        // Whatever the text, the reader answers with expressions or with its own refusal: no
        // other exception, and what it read writes back to a text that reads the same.
        const string alphabet = "()\"|;:#.\\ \t\r\n\0a1x0b-+_'\u00e9\ud83d\ude00";
        var random = new Random(20261122);

        for (var round = 0; round < 20_000; round++)
        {
            var builder = new StringBuilder();
            var length = random.Next(0, 40);
            for (var index = 0; index < length; index++)
                builder.Append(alphabet[random.Next(alphabet.Length)]);

            var text = builder.ToString();
            IReadOnlyList<SmtExpression> expressions;
            try
            {
                expressions = SmtReader.Read(text, SmtReaderLimits.Problem);
            }
            catch (SmtSyntaxException)
            {
                continue;
            }

            var written = string.Join("\n", expressions.Select(expression => expression.ToString()));
            var again = SmtReader.Read(written, SmtReaderLimits.Problem);

            Assert.That(again, Has.Count.EqualTo(expressions.Count), () => Show(text));
            for (var index = 0; index < expressions.Count; index++)
                Assert.That(again[index].Is(expressions[index]), Is.True, () => Show(text));
        }
    }

    #endregion

    #region Tools

    private static SmtExpression ReadOne(string text, SmtReaderLimits? limits = null)
    {
        return SmtReader.Read(text, limits ?? SmtReaderLimits.Problem).Single();
    }

    private static void AssertRefused(string text, SmtReaderLimits limits, SmtSyntaxFault fault)
    {
        var refusal = Assert.Throws<SmtSyntaxException>(() => SmtReader.Read(text, limits))!;
        Assert.That(refusal.Fault, Is.EqualTo(fault));
    }

    private static string Show(string text)
    {
        return string.Join(' ', text.Select(character => ((int)character).ToString("X4")));
    }

    #endregion
}
