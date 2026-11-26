using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// What a refusal shows of a token (a command's name, a field's name): one line of text a lab's
/// documents and screens show as it is. Every character that breaks a line, turns the direction
/// of the text, hides itself, or is half of a pair that stands alone, shows as a blank; every
/// other character, a whole pair among them, shows as it is. The characters are made from their
/// numbers here, so that the file holds none of them.
/// </summary>
[TestFixture]
public sealed class SmtRefusalsTests
{
    #region Token Tests

    [Test]
    public void EveryCharacterThatBreaksOrTurnsALineIsShownAsABlankTest()
    {
        var characters = new (int Code, string Name)[]
        {
            (0x0009, "tab"), (0x000A, "line feed"), (0x000D, "carriage return"), (0x007F, "delete"),
            (0x0085, "next line"), (0x009B, "control sequence introducer"),
            (0x2028, "line separator"), (0x2029, "paragraph separator"),
            (0x202E, "right-to-left override"), (0x202D, "left-to-right override"), (0x2067, "right-to-left isolate"),
            (0x200F, "right-to-left mark"), (0x200B, "zero width space"), (0x00AD, "soft hyphen"), (0xFEFF, "byte order mark")
        };

        Assert.Multiple(() =>
        {
            foreach (var (code, name) in characters)
                Assert.That(SmtRefusals.Token($"a{(char)code}b"), Is.EqualTo("a b"), name);
        });
    }

    [Test]
    public void AHalfOfAPairThatStandsAloneIsShownAsABlankTest()
    {
        var high = (char)0xD83D;
        var low = (char)0xDE00;

        Assert.Multiple(() =>
        {
            Assert.That(SmtRefusals.Token($"a{high}b"), Is.EqualTo("a b"), "a high half alone");
            Assert.That(SmtRefusals.Token($"a{low}b"), Is.EqualTo("a b"), "a low half alone");
            Assert.That(SmtRefusals.Token($"a{low}{high}b"), Is.EqualTo("a  b"), "two halves the wrong way round");
            Assert.That(SmtRefusals.Token($"ab{high}"), Is.EqualTo("ab "), "a high half at the end");
            Assert.That(SmtRefusals.Token($"a{high}{low}b"), Is.EqualTo($"a{high}{low}b"), "a whole pair is a character like any other");
        });
    }

    [Test]
    public void AFormatCharacterOutsideTheFirstPlaneIsShownAsBlanksTest()
    {
        // A tag character: a pair of halves whose character hides itself.
        var tag = char.ConvertFromUtf32(0xE0041);

        Assert.That(SmtRefusals.Token($"a{tag}b"), Is.EqualTo("a  b"));
    }

    [Test]
    public void AnOrdinaryTokenIsShownAsItIsTest()
    {
        var accented = $"caf{(char)0x00E9}";

        Assert.Multiple(() =>
        {
            Assert.That(SmtRefusals.Token("check-sat-assuming"), Is.EqualTo("check-sat-assuming"));
            Assert.That(SmtRefusals.Token("two words"), Is.EqualTo("two words"));
            Assert.That(SmtRefusals.Token(accented), Is.EqualTo(accented));
        });
    }

    [Test]
    public void ARefusedCommandsNameIsShownOnOneLineTest()
    {
        // Through the sanitizer, as a lab meets it: a command name between bars may hold any
        // character but a bar and a backslash.
        var name = $"one{(char)0x2028}two{(char)0x202E}three{(char)0x0085}four";

        var refusal = SmtSanitizerChecks.Refuse($"(|{name}| 1)", SmtRefusalCodes.COMMAND_NOT_ALLOWED);

        Assert.That(refusal.Command, Is.EqualTo("one two three four"));
    }

    #endregion
}
