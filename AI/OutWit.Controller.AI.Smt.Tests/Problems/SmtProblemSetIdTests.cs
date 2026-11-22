using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// The id of a row: what an id is made of, and that it names one row of its set.
/// </summary>
[TestFixture]
public class SmtProblemSetIdTests
{
    #region Constants

    private const string ID_RULE = "'id' must be 1 to 64 characters, each a letter A-Z or a-z, a digit, or one of . _ - / + : @ =";

    #endregion

    #region Shape Tests

    [Test]
    public void IdIsMadeOfLettersDigitsAndEightMarksTest()
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        const string others = "0123456789._-/+:@=";

        foreach (var id in new[] { letters, others, "a", "0", ".", "=", "cage-6x6-000412", "qf_lia/0841.smt2", "user@lab:set+1=a", new string('x', 64), "-leading", "/", "..", "a/../b" })
            Assert.That(SmtProblemSets.Accept($"{{\"id\":\"{id}\",\"smt2\":\"(assert true)\"}}").Id, Is.EqualTo(id));

        // Every allowed character on its own, and every other printable ASCII character refused.
        for (var code = 32; code < 127; code++)
        {
            var character = (char)code;
            var json = character is '"' or '\\' ? "\\" + character : character.ToString();
            var row = SmtProblemSets.Read($"{{\"id\":\"a{json}b\",\"smt2\":\"(assert true)\"}}").Rows.Single();

            Assert.That(row.IsAccepted, Is.EqualTo((letters + others).Contains(character)), $"U+{code:X4}");
            if (!row.IsAccepted)
                Assert.That((row.Refusal!.Code, row.Refusal.Reason, row.Refusal.File), Is.EqualTo((SmtRefusalCodes.ID_INVALID, ID_RULE, "")), $"U+{code:X4}");
        }
    }

    [Test]
    public void IdOfAnotherLengthOrOtherCharactersRefusesTheRowTest()
    {
        var escape = "\\" + "u";
        var ids = new[]
        {
            "", new string('x', 65), new string('x', 5_000), "a b", " a", "a ", "a\\tb", "a\\nb", "caf\u00e9", "\u0430bc", "a,b", "a;b", "a#b", "a?b", "a*b", "a\\\\b", "a\\\"b",
            "a|b", "(a)", "a'b", "a~b", "a%b", "a&b", "<a>", "[a]", "{a}", escape + "0000", "a" + escape + "007f", escape + "d83d" + escape + "de00"
        };

        foreach (var id in ids)
        {
            var refusal = SmtProblemSets.Refuse($"{{\"id\":\"{id}\",\"smt2\":\"(assert true)\"}}", SmtRefusalCodes.ID_INVALID);

            Assert.That(refusal.Reason, Is.EqualTo(ID_RULE), id);
            Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "")), "an id that is none is not shown as one");
        }
    }

    [Test]
    public void IdWrittenWithEscapesIsItsCharactersTest()
    {
        var escape = "\\" + "u";

        Assert.That(SmtProblemSets.Accept($"{{\"id\":\"{escape}0061{escape}002fb\",\"smt2\":\"(assert true)\"}}").Id, Is.EqualTo("a/b"));
        Assert.That(SmtProblemSets.Accept("{\"id\":\"a\\/b\",\"smt2\":\"(assert true)\"}").Id, Is.EqualTo("a/b"));
    }

    #endregion

    #region Uniqueness Tests

    [Test]
    public void IdNamesOneRowOfItsSetTest()
    {
        var rows = SmtProblemSets.Read(
            "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n" +
            "{\"id\":\"b\",\"smt2\":\"(assert true)\"}\n" +
            "\n" +
            "{\"id\":\"a\",\"smt2\":\"(assert false)\"}\n" +
            "{\"id\":\"A\",\"smt2\":\"(assert true)\"}\n" +
            "{\"id\":\"b\",\"smt2\":\"(assert true)\"}\n" +
            "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n").Rows;

        Assert.That(rows.Select(row => row.IsAccepted), Is.EqualTo(new[] { true, true, false, true, false, false }));

        var duplicates = rows.Where(row => !row.IsAccepted).Select(row => row.Refusal!).ToList();
        Assert.That(duplicates.Select(refusal => refusal.Code), Is.All.EqualTo(SmtRefusalCodes.ID_DUPLICATE));
        Assert.That(duplicates.Select(refusal => (refusal.Row, refusal.File, refusal.Line)), Is.EqualTo(new[] { (4, "a", 0), (6, "b", 0), (7, "a", 0) }));
        Assert.That(duplicates.Select(refusal => refusal.Reason), Is.EqualTo(new[]
        {
            "the id is the id of the row on line 1: an id names one row of a set",
            "the id is the id of the row on line 2: an id names one row of a set",
            "the id is the id of the row on line 1: an id names one row of a set"
        }), "the first row with an id keeps it");
    }

    [Test]
    public void RefusedRowKeepsItsIdTest()
    {
        // Two rows of one set have one id: that is wrong whatever became of the first of them,
        // and a refusal is found by its row's id.
        var rows = SmtProblemSets.Read(
            "{\"id\":\"a\",\"smt2\":\"(push 1)\"}\n" +
            "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n" +
            "{\"id\":\"b\",\"smt2\":\"(assert true)\",\"nope\":1}\n" +
            "{\"id\":\"b\",\"smt2\":\"(assert true)\"}\n" +
            "{\"id\":\"c\"}\n" +
            "{\"id\":\"c\",\"smt2\":\"(assert true)\"}\n").Rows;

        Assert.That(rows.Select(row => row.Refusal!.Code), Is.EqualTo(new[]
        {
            SmtRefusalCodes.COMMAND_NOT_ALLOWED, SmtRefusalCodes.ID_DUPLICATE, SmtRefusalCodes.FIELD_NOT_ALLOWED, SmtRefusalCodes.ID_DUPLICATE,
            SmtRefusalCodes.FIELD_MISSING, SmtRefusalCodes.ID_DUPLICATE
        }));
        Assert.That(rows.Select(row => row.Refusal!.File), Is.EqualTo(new[] { "a", "a", "b", "b", "c", "c" }));
    }

    [Test]
    public void RowThatDoesNotReadSaysSoBeforeItIsADuplicateTest()
    {
        var rows = SmtProblemSets.Read("{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n{\"id\":\"a\",\"smt2\":\"(assert true)\",\"tags\":5}\n{\"id\":\"a\"}").Rows;

        Assert.That(rows[1].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.FIELD_INVALID), "the row's own fault first");
        Assert.That(rows[2].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.ID_DUPLICATE), "an id that is taken before a field that is missing");
        Assert.That(rows.Skip(1).Select(row => row.Refusal!.File), Is.All.EqualTo("a"));
    }

    [Test]
    public void LineThatIsNoRowTakesNoIdTest()
    {
        var rows = SmtProblemSets.Read("{\"id\":\"a\",\"smt2\":\"(assert true)\"\n{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n{\"id\":\"b b\",\"smt2\":\"(assert true)\"}").Rows;

        Assert.That(rows.Select(row => row.IsAccepted), Is.EqualTo(new[] { false, true, false }));
        Assert.That(rows[0].Refusal!.File, Is.Empty, "a line that is no JSON object has no id, whatever stands in it");
    }

    [Test]
    public void ManyIdsAreAllToldApartTest()
    {
        // Ids are remembered by a few bytes each: every one of many is still its own.
        const int count = 50_000;
        var lines = new System.Text.StringBuilder();
        for (var index = 0; index < count; index++)
            lines.Append("{\"id\":\"p").Append(index).Append("\",\"smt2\":\"(assert true)\"}\n");

        lines.Append("{\"id\":\"p31415\",\"smt2\":\"(assert true)\"}\n");

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(lines.ToString()));
        var reader = new SmtProblemSetReader(stream, new OutWit.Controller.AI.Smt.Model.SmtOptionsData());
        SmtProblemSetRow? last = null;
        while (reader.TryRead(out var row))
            last = row;

        Assert.That((reader.Summary.Accepted, reader.Summary.Refused), Is.EqualTo((count, 1)));
        Assert.That(last!.Refusal!.Reason, Is.EqualTo("the id is the id of the row on line 31416: an id names one row of a set"));
    }

    #endregion
}
