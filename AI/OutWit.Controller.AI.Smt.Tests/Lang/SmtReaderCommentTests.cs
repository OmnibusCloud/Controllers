using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// Where a comment ends. The standard ends a comment at a line feed or at a carriage return; a
/// solver that ends a comment at the line feed only reads what follows a lone carriage return as
/// comment still. Where the two would read two texts, the reader refuses the text: a carriage
/// return in a comment is followed by the line feed that ends the comment.
/// </summary>
[TestFixture]
public class SmtReaderCommentTests
{
    #region Comment Tests

    [Test]
    public void ReadRefusesACarriageReturnInACommentThatNoLineFeedFollowsTest()
    {
        // At the carriage return: at the start of a text, in the middle and at its very end.
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
            Assert.That(refusal.Message, Is.EqualTo($"line {line}, column {column}: a carriage return in a comment with no line feed after it"));
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
    public void ReadEndsACommentAtALineFeedAndAtTheEndOfTheTextTest()
    {
        Assert.That(SmtReader.Read("a ; x (hidden)\nb ; the last one, without a line end", SmtReaderLimits.Problem).Select(expression => expression.Text), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(SmtReader.Read(";", SmtReaderLimits.Problem), Is.Empty);
    }

    [Test]
    public void ReadTakesALoneCarriageReturnOutsideACommentAsABlankTest()
    {
        var expressions = SmtReader.Read("a\r(b\rc)\r", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.ToString()), Is.EqualTo(new[] { "a", "(b c)" }));
        Assert.That(SmtReader.Read("\"a\rb\" |c\rd|", SmtReaderLimits.Problem).Select(expression => expression.Text), Is.EqualTo(new[] { "a\rb", "c\rd" }));
    }

    [Test]
    public void ReadTakesASemicolonInsideALiteralForNoCommentTest()
    {
        // No comment starts inside a string or a quoted symbol: a carriage return after a
        // semicolon there is the literal's own.
        var expressions = SmtReader.Read("\"a ; b\rc\" |d ; e\rf|", SmtReaderLimits.Problem);

        Assert.That(expressions.Select(expression => expression.Text), Is.EqualTo(new[] { "a ; b\rc", "d ; e\rf" }));
    }

    #endregion

    #region Tools

    private static string Show(string text)
    {
        return string.Join(' ', text.Select(character => ((int)character).ToString("X4")));
    }

    #endregion
}
