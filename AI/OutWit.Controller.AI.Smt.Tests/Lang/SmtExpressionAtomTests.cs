using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// An atom a program makes is written as its text, so the text must be a token of its kind:
/// written back, an s-expression reads as the same s-expression again. A text that is no token
/// of the kind (a numeral that closes a list and opens a command, a keyword with a blank) is
/// refused when the atom is made; a string keeps any content, its quotes are its writer's.
/// </summary>
[TestFixture]
public sealed class SmtExpressionAtomTests
{
    #region Atom Tests

    [Test]
    public void ATextThatIsNoTokenOfItsKindIsRefusedTest()
    {
        var texts = new (SmtExpressionKind Kind, string Text)[]
        {
            (SmtExpressionKind.Numeral, "1) (exit"),
            (SmtExpressionKind.Numeral, "1 2"),
            (SmtExpressionKind.Numeral, ""),
            (SmtExpressionKind.Numeral, "007"),
            (SmtExpressionKind.Numeral, "-1"),
            (SmtExpressionKind.Numeral, "1.5"),
            (SmtExpressionKind.Decimal, "1.2.3"),
            (SmtExpressionKind.Decimal, "15"),
            (SmtExpressionKind.Hexadecimal, "#xzz"),
            (SmtExpressionKind.Hexadecimal, "#b01"),
            (SmtExpressionKind.Binary, "#b012"),
            (SmtExpressionKind.Keyword, ":"),
            (SmtExpressionKind.Keyword, ":a b"),
            (SmtExpressionKind.Keyword, "named"),
            (SmtExpressionKind.Keyword, ":rlimit 1) (exit")
        };

        Assert.Multiple(() =>
        {
            foreach (var (kind, text) in texts)
                Assert.Throws<ArgumentException>(() => SmtExpression.Atom(kind, text), $"{kind} '{text}'");
        });
    }

    [Test]
    public void ATokenOfItsKindIsTakenAndReadsBackTest()
    {
        var texts = new (SmtExpressionKind Kind, string Text)[]
        {
            (SmtExpressionKind.Numeral, "0"),
            (SmtExpressionKind.Numeral, "4294967295"),
            (SmtExpressionKind.Numeral, "340282366920938463463374607431768211456"),
            (SmtExpressionKind.Decimal, "1.50"),
            (SmtExpressionKind.Hexadecimal, "#x0F"),
            (SmtExpressionKind.Binary, "#b0101"),
            (SmtExpressionKind.Keyword, ":random-seed")
        };

        Assert.Multiple(() =>
        {
            foreach (var (kind, text) in texts)
            {
                var atom = SmtExpression.Atom(kind, text);
                var back = SmtReader.Read(atom.ToString(), SmtReaderLimits.Unbounded).Single();

                Assert.That((back.Kind, back.Text), Is.EqualTo((kind, text)), $"{kind} '{text}'");
            }
        });
    }

    [Test]
    public void AStringKeepsAnyContentTest()
    {
        var content = "\")) (exit) (echo \"x";
        var atom = SmtExpression.Atom(SmtExpressionKind.String, content);
        var back = SmtReader.Read($"(echo {atom})", SmtReaderLimits.Unbounded).Single();

        Assert.Multiple(() =>
        {
            Assert.That(atom.Text, Is.EqualTo(content));
            Assert.That(back.Items, Has.Count.EqualTo(2), "one command with one string");
            Assert.That(back.Items[1].Text, Is.EqualTo(content));
        });
    }

    [Test]
    public void AnOptionsValueCannotBecomeACommandTest()
    {
        // The value of an option is written inside the command that sets it: a numeral made of
        // a text that closes the command would have made two.
        Assert.Throws<ArgumentException>(() => SmtScripts.SetOption(":random-seed", SmtExpression.Atom(SmtExpressionKind.Numeral, "7) (exit")));
    }

    #endregion
}
