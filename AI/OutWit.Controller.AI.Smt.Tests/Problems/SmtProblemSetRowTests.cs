using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A row, strictly: a line the schema of a row would not take is refused, with a fixed code and
/// by the field's name. What is no row at all, what is no field of a row, and what is no value
/// of a field.
/// </summary>
[TestFixture]
public class SmtProblemSetRowTests
{
    #region Constants

    private const string NOT_AN_OBJECT = "the line is not one JSON object";

    /// <summary>The start of a row whose point is one more field.</summary>
    private const string ROW = """{"id":"a","smt2":"(assert true)" """;

    #endregion

    #region Malformed Row Tests

    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("[{\"id\":\"a\",\"smt2\":\"(assert true)\"}]")]
    [TestCase("\"text\"")]
    [TestCase("42")]
    [TestCase("null")]
    [TestCase("{\"id\":\"a\"")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)\"} trailing")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)\"}{\"id\":\"b\",\"smt2\":\"(assert true)\"}")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)\",}")]
    [TestCase("{'id':'a','smt2':'(assert true)'}")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)\" /* a comment */}")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert true)\",\"tags\":[[[[[[[[[1]]]]]]]]]}")]
    [TestCase("{\"id\":\"a\",\"smt2\":\"(assert \\x true)\"}")]
    public void LineThatIsNotOneJsonObjectIsNoRowTest(string line)
    {
        var refusal = SmtProblemSets.Refuse(line, SmtRefusalCodes.ROW_MALFORMED);

        Assert.That(refusal.Reason, Is.EqualTo(NOT_AN_OBJECT));
        Assert.That((refusal.Row, refusal.Line, refusal.File, refusal.Command), Is.EqualTo((1, 0, "", "")), "nothing of such a line is readable, its id included");
    }

    [Test]
    public void LineThatIsNotUtf8IsNoRowTest()
    {
        // Bytes that are no UTF-8: a lone continuation byte, a byte no UTF-8 has, an overlong
        // form, a surrogate written as bytes, a sequence cut short. In a string, in a name,
        // between two tokens.
        var cases = new byte[][]
        {
            [0x80], [0xFF], [0xC0, 0xAF], [0xED, 0xA0, 0x80], [0xE2, 0x82]
        };

        foreach (var bad in cases)
        {
            foreach (var (before, after) in new[] { ("{\"id\":\"a\",\"smt2\":\"(assert true)\",\"tags\":\"", "\"}"), ("{\"id\":\"a\",\"", "\":1}"), ("{\"id\":\"a\",", "\"smt2\":\"(assert true)\"}") })
            {
                var bytes = Encoding.UTF8.GetBytes(before).Concat(bad).Concat(Encoding.UTF8.GetBytes(after + "\n" + ROW + "}")).ToArray();

                var rows = SmtProblemSets.Read(bytes).Rows;

                Assert.That(rows[0].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.ROW_MALFORMED));
                Assert.That(rows[0].Refusal!.Reason, Is.EqualTo("the line is not valid UTF-8"));
                Assert.That(rows[0].Refusal!.File, Is.Empty);
                Assert.That(rows[1].IsAccepted, Is.True, "the row after it is read");
            }
        }
    }

    [Test]
    public void FieldGivenTwiceRefusesTheRowTest()
    {
        var refusal = SmtProblemSets.Refuse(ROW + ""","smt2":"(assert false)"}""", SmtRefusalCodes.ROW_MALFORMED);

        Assert.That(refusal.Reason, Is.EqualTo("the field 'smt2' is given twice"));
        Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "a")));

        // Every field, each time with a value it takes: once more is once too many.
        var fields = new (string Name, string Value)[]
        {
            ("query", "\"check\""), ("values", "[\"x\"]"), ("project", "[\"x\"]"), ("max_models", "2"), ("goal", "\"x\""), ("expected", "\"sat\""), ("tags", "\"t\"")
        };

        foreach (var (name, value) in fields)
        {
            Assert.That(SmtProblemSets.Refuse(ROW + $",\"{name}\":{value},\"{name}\":{value}}}", SmtRefusalCodes.ROW_MALFORMED).Reason, Is.EqualTo($"the field '{name}' is given twice"));
            Assert.That(SmtProblemSets.Refuse($"{{\"{name}\":{value},\"id\":\"a\",\"smt2\":\"(assert true)\",\"{name}\":null}}", SmtRefusalCodes.ROW_MALFORMED).Reason, Is.EqualTo($"the field '{name}' is given twice"));

            // The first fault of the line is the one named: here the value that does not read.
            Assert.That(SmtProblemSets.Refuse(ROW + $",\"{name}\":null,\"{name}\":{value}}}", SmtRefusalCodes.FIELD_INVALID).Reason, Does.StartWith($"'{name}' must be"));
        }
    }

    [Test]
    public void IdGivenTwiceIsNoIdOfTheRowTest()
    {
        var refusal = SmtProblemSets.Refuse("""{"id":"a","id":"b","smt2":"(assert true)"}""", SmtRefusalCodes.ROW_MALFORMED);

        Assert.That(refusal.Reason, Is.EqualTo("the field 'id' is given twice"));
        Assert.That(refusal.File, Is.Empty);

        // Neither id is taken: the rows that have them later keep them.
        var rows = SmtProblemSets.Read("{\"id\":\"a\",\"id\":\"b\",\"smt2\":\"(assert true)\"}\n{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n{\"id\":\"b\",\"smt2\":\"(assert true)\"}").Rows;
        Assert.That(rows.Select(row => row.IsAccepted), Is.EqualTo(new[] { false, true, true }));
    }

    #endregion

    #region Unknown Field Tests

    [TestCase("timeout", "5")]
    [TestCase("expected_", "\"sat\"")]
    [TestCase("Id", "\"b\"")]
    [TestCase("SMT2", "\"(assert false)\"")]
    [TestCase("smt2_file", "\"a.smt2\"")]
    [TestCase("maxModels", "2")]
    [TestCase("", "1")]
    [TestCase(" tags", "\"x\"")]
    [TestCase("x", "null")]
    [TestCase("x", "{\"id\":\"nested\"}")]
    [TestCase("x", "[1,[2,[3]]]")]
    public void FieldThatIsNoneOfARowsRefusesTheRowTest(string name, string value)
    {
        var refusal = SmtProblemSets.Refuse(ROW + $",\"{name}\":{value}}}", SmtRefusalCodes.FIELD_NOT_ALLOWED);

        Assert.That(refusal.Reason, Is.EqualTo($"'{name}' is no field of a row"));
        Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "a")));
    }

    [Test]
    public void UnknownFieldIsShownCutAndOnOneLineTest()
    {
        var longName = new string('q', 100);
        var veryLongName = new string('z', 5_000);
        var escape = "\\" + "u";

        Assert.That(SmtProblemSets.Refuse(ROW + $",\"{longName}\":1}}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason,
            Is.EqualTo("'" + new string('q', 61) + "...' is no field of a row"));
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"{veryLongName}\":1}}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason,
            Is.EqualTo("a row has no field with such a name (the name is too long or no Unicode text to be shown)"));
        Assert.That(SmtProblemSets.Refuse(ROW + ",\"two\\nlines\\tand a tab\":1}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason,
            Is.EqualTo("'two lines and a tab' is no field of a row"));
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"{escape}d83d\":1}}", SmtRefusalCodes.FIELD_NOT_ALLOWED).Reason,
            Is.EqualTo("a row has no field with such a name (the name is too long or no Unicode text to be shown)"));
    }

    [Test]
    public void KnownFieldWrittenWithEscapesIsTheFieldTest()
    {
        // A JSON name is its characters, however they are written.
        var escape = "\\" + "u";

        var data = SmtProblemSets.Accept($"{{\"{escape}0069d\":\"a\",\"smt{escape}0032\":\"(assert true)\"}}");

        Assert.That(data.Id, Is.EqualTo("a"));
        Assert.That(data.Text, Is.EqualTo("(assert true)\n"));
    }

    #endregion

    #region Field Value Tests

    [TestCase("id", "'id' must be a string")]
    [TestCase("smt2", "'smt2' must be a string")]
    [TestCase("query", "'query' must be \"check\", \"enumerate\" or \"prove\"")]
    [TestCase("values", "'values' must be an array of strings")]
    [TestCase("project", "'project' must be an array of strings")]
    [TestCase("max_models", "'max_models' must be an integer from 1 to 1000")]
    [TestCase("goal", "'goal' must be a string")]
    [TestCase("expected", "'expected' must be \"sat\" or \"unsat\"")]
    [TestCase("tags", "'tags' must be a string")]
    public void ValueOfAnotherTypeRefusesTheRowTest(string field, string reason)
    {
        // null is a value of another type: a field that is not meant is left out.
        var others = new List<string> { "null", "true", "false", "{}", "{\"a\":[1,{\"b\":null}]}" };
        others.AddRange(field == "max_models" ? new[] { "\"2\"", "[2]" } : new[] { "5", "1.5" });
        others.AddRange(field is "values" or "project" ? new[] { "\"x\"" } : new[] { "[]", "[\"x\"]" });

        foreach (var value in others)
        {
            var line = field is "id" or "smt2"
                ? (field == "id" ? $"{{\"id\":{value},\"smt2\":\"(assert true)\"}}" : $"{{\"id\":\"a\",\"smt2\":{value}}}")
                : ROW + $",\"{field}\":{value}}}";

            var refusal = SmtProblemSets.Refuse(line, SmtRefusalCodes.FIELD_INVALID);

            Assert.That(refusal.Reason, Is.EqualTo(reason), value);
            Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, field == "id" ? "" : "a")), value);
        }
    }

    [TestCase("\"solve\"")]
    [TestCase("\"Check\"")]
    [TestCase("\"CHECK\"")]
    [TestCase("\"check \"")]
    [TestCase("\"\"")]
    [TestCase("\"check-sat\"")]
    public void QueryIsOneOfThreeWordsTest(string value)
    {
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"query\":{value}}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'query' must be \"check\", \"enumerate\" or \"prove\""));
    }

    [TestCase("\"unknown\"")]
    [TestCase("\"SAT\"")]
    [TestCase("\"\"")]
    [TestCase("\"sat \"")]
    [TestCase("\"timeout\"")]
    public void ExpectedIsSatOrUnsatTest(string value)
    {
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"expected\":{value}}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'expected' must be \"sat\" or \"unsat\""));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1001")]
    [TestCase("1.5")]
    [TestCase("1e400")]
    [TestCase("99999999999999999999")]
    [TestCase("-99999999999999999999999999999999999999")]
    [TestCase("0.9999")]
    public void MaxModelsIsAnIntegerFromOneToAThousandTest(string value)
    {
        var line = SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[\"x\"],\"max_models\":{value}");

        Assert.That(SmtProblemSets.Refuse(line, SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'max_models' must be an integer from 1 to 1000"));
    }

    [TestCase("1", 1)]
    [TestCase("1000", 1000)]
    [TestCase("2.0", 2)]
    [TestCase("1e2", 100)]
    [TestCase("1.0e3", 1000)]
    [TestCase("20e-1", 2)]
    public void MaxModelsTakesAnIntegerHoweverItIsWrittenTest(string value, int models)
    {
        Assert.That(SmtProblemSets.Accept(SmtProblemSets.Row($"\"query\":\"enumerate\",\"project\":[\"x\"],\"max_models\":{value}")).MaxModels, Is.EqualTo(models));
    }

    [Test]
    public void ListOfTermsHoldsOneToSoManyStringsTest()
    {
        var most = string.Join(",", Enumerable.Repeat("\"x\"", SmtProblemSetLimits.MAX_VALUES));

        Assert.That(SmtProblemSets.Accept(SmtProblemSets.Row($"\"values\":[{most}]")).Values, Has.Count.EqualTo(256));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"\"values\":[{most},\"x\"]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values' must hold at most 256 items"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row("\"values\":[]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values' must hold at least one item"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row("\"values\":[\"x\",5,\"y\"]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values[1]' must be a string"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row("\"values\":[null]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values[0]' must be a string"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row("\"values\":[\"x\",[\"y\"],{\"z\":1}]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values[1]' must be a string"));

        var enumerate = "\"query\":\"enumerate\",\"project\":";
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"{enumerate}[{most},\"y\"]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'project' must hold at most 256 items"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"{enumerate}[]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'project' must hold at least one item"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"{enumerate}[true]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'project[0]' must be a string"));
    }

    [Test]
    public void TagsAreAShortTextWithoutControlCharactersTest()
    {
        const string reason = "'tags' must be at most 256 bytes of UTF-8 without control characters";
        var escape = "\\" + "u";
        var accented = "\u00e9";

        Assert.That(SmtProblemSets.Accept(ROW + $",\"tags\":\"{new string('t', 256)}\"}}").Tags, Has.Length.EqualTo(256));
        Assert.That(SmtProblemSets.Accept(ROW + $",\"tags\":\"{string.Concat(Enumerable.Repeat(accented, 128))}\"}}").Tags, Has.Length.EqualTo(128), "256 bytes");
        Assert.That(SmtProblemSets.Accept(ROW + ",\"tags\":\"\"}").Tags, Is.Empty);
        Assert.That(SmtProblemSets.Accept(ROW + ",\"tags\":\"kit=b; cage 6x6, \\\"quoted\\\" \\\\ /\"}").Tags, Is.EqualTo("kit=b; cage 6x6, \"quoted\" \\ /"));

        Assert.That(SmtProblemSets.Refuse(ROW + $",\"tags\":\"{new string('t', 257)}\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo(reason));
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"tags\":\"{string.Concat(Enumerable.Repeat(accented, 128))}t\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo(reason), "257 bytes in 129 characters");
        Assert.That(SmtProblemSets.Refuse(ROW + $",\"tags\":\"{new string('t', 5_000)}\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo(reason));

        foreach (var control in new[] { "\\n", "\\t", "\\r", escape + "0000", escape + "0001", escape + "001f", escape + "007f", escape + "0085", escape + "009f" })
            Assert.That(SmtProblemSets.Refuse(ROW + $",\"tags\":\"a{control}b\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo(reason), control);

        Assert.That(SmtProblemSets.Refuse(ROW + $",\"tags\":\"{escape}d83d\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo(reason), "half of a surrogate pair");
    }

    [Test]
    public void TextThatIsNotUnicodeRefusesTheRowByTheFieldsNameTest()
    {
        // Half of a surrogate pair written as an escape: valid JSON, and no text.
        var half = "\\" + "ud83d";
        var pair = "\\" + "ud83d" + "\\" + "ude00";

        Assert.That(SmtProblemSets.Refuse($"{{\"id\":\"a\",\"smt2\":\"(assert {half})\"}}", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'smt2' is not valid Unicode text"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"\"values\":[\"x\",\"{half}\"]"), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'values[1]' is not valid Unicode text"));
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"prove\",\"goal\":\"{half}\""), SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'goal' is not valid Unicode text"));
        Assert.That(SmtProblemSets.Refuse($"{{\"id\":\"{half}\",\"smt2\":\"(assert true)\"}}", SmtRefusalCodes.ID_INVALID).File, Is.Empty);
        Assert.That(SmtProblemSets.Refuse(SmtProblemSets.Row($"\"query\":\"{half}\""), SmtRefusalCodes.FIELD_INVALID).Reason, Does.StartWith("'query' must be"));

        // A whole pair is a character: of a quoted symbol here.
        Assert.That(SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"(declare-const |{pair}| Bool)\"}}").Text, Is.EqualTo("(declare-const |\ud83d\ude00| Bool)\n"));
    }

    #endregion

    #region Order Tests

    [Test]
    public void TheFirstFaultOfTheLineIsTheOneNamedAndTheIdIsStillReadTest()
    {
        var unknownFirst = SmtProblemSets.Refuse("""{"x":1,"tags":5,"id":"late","smt2":"(assert true)"}""", SmtRefusalCodes.FIELD_NOT_ALLOWED);
        var typeFirst = SmtProblemSets.Refuse("""{"tags":5,"x":1,"id":"late","smt2":"(assert true)"}""", SmtRefusalCodes.FIELD_INVALID);

        Assert.That((unknownFirst.Reason, unknownFirst.File), Is.EqualTo(("'x' is no field of a row", "late")));
        Assert.That((typeFirst.Reason, typeFirst.File), Is.EqualTo(("'tags' must be a string", "late")));
    }

    [Test]
    public void FieldThatDoesNotReadComesBeforeAFieldThatIsMissingTest()
    {
        Assert.That(SmtProblemSets.Refuse("""{"id":"a","goal":5}""", SmtRefusalCodes.FIELD_INVALID).Reason, Is.EqualTo("'goal' must be a string"));
        Assert.That(SmtProblemSets.Refuse("""{"smt2":"(assert true)","nope":1}""", SmtRefusalCodes.FIELD_NOT_ALLOWED).File, Is.Empty);
    }

    [Test]
    public void RowWithoutItsIdOrItsProblemIsRefusedTest()
    {
        Assert.That(SmtProblemSets.Refuse("{}", SmtRefusalCodes.FIELD_MISSING).Reason, Is.EqualTo("'id' is missing: a row names its problem"));
        Assert.That(SmtProblemSets.Refuse("""{"smt2":"(assert true)"}""", SmtRefusalCodes.FIELD_MISSING).Reason, Is.EqualTo("'id' is missing: a row names its problem"));

        var refusal = SmtProblemSets.Refuse("""{"id":"a","query":"check"}""", SmtRefusalCodes.FIELD_MISSING);
        Assert.That(refusal.Reason, Is.EqualTo("'smt2' is missing: a row holds the text of its problem"));
        Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 0, "a")));
    }

    #endregion
}
