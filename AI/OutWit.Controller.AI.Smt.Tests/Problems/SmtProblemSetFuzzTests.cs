using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A fuzz over rows with a fixed seed: valid rows with fields dropped, doubled, renamed and
/// given values of other types, lines cut short and run together, and raw bytes put in. Whatever
/// comes out is a row with a problem that travels as its own canonical text, or a refusal with a
/// code of the contract; nothing else, and no exception.
/// </summary>
[TestFixture]
public class SmtProblemSetFuzzTests
{
    #region Constants

    private const int SEED = 20261123;
    private const int ROUNDS = 20_000;
    private const string PROBLEM = "(set-logic QF_LIA)(set-info :status sat)(declare-const x Int)(declare-const y Int)(assert (> x y))(check-sat)";

    /// <summary>Valid rows of every query, each as its fields: a name and a value as JSON.</summary>
    private static readonly (string Name, string Value)[][] ROWS =
    [
        [("id", "\"a\""), ("smt2", $"\"{PROBLEM}\"")],
        [("id", "\"b\""), ("smt2", $"\"{PROBLEM}\""), ("query", "\"check\""), ("values", "[\"x\",\"(+ x y)\"]"), ("expected", "\"sat\""), ("tags", "\"kit a\"")],
        [("id", "\"c\""), ("smt2", $"\"{PROBLEM}\""), ("query", "\"enumerate\""), ("project", "[\"x\",\"y\"]"), ("max_models", "5"), ("expected", "\"unsat\"")],
        [("id", "\"d\""), ("smt2", $"\"{PROBLEM}\""), ("query", "\"prove\""), ("goal", "\"(>= x y)\""), ("tags", "\"\"")],
        [("id", "\"e/1\""), ("query", "\"enumerate\""), ("project", "[\"|x|\"]"), ("smt2", $"\"{PROBLEM}\"")]
    ];

    private static readonly string[] NAMES = ["id", "smt2", "query", "values", "project", "max_models", "goal", "expected", "tags", "Id", "smt", "timeout", "", "x"];

    private static readonly byte[] RAW = [0x00, 0x09, 0x0D, 0x22, 0x5C, 0x7B, 0x7D, 0x5B, 0x5D, 0x2C, 0x3A, 0x20, 0x80, 0xC0, 0xE2, 0xFF, 0x7F];

    #endregion

    #region Fuzz Tests

    [Test]
    public void MutatedRowIsAProblemOrARefusalOfTheContractTest()
    {
        var random = new Random(SEED);
        var values = Values();
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var accepted = 0;
        var rows = 0;
        var watch = Stopwatch.StartNew();

        for (var round = 0; round < ROUNDS; round++)
        {
            var set = Set(random, values);
            using var stream = new MemoryStream(set, writable: false);
            var reader = new SmtProblemSetReader(stream, new SmtOptionsData { Query = (SmtQuery)random.Next(3), MaxModels = random.Next(3) });

            while (reader.TryRead(out var row))
            {
                SmtProblemSets.Hold(row);
                rows++;

                if (row.IsAccepted)
                    accepted++;
                else
                    codes[row.Refusal!.Code] = codes.GetValueOrDefault(row.Refusal.Code) + 1;
            }

            Assert.That(reader.Summary.BytesRead, Is.EqualTo(set.Length));
            Assert.That(reader.Summary.Accepted + reader.Summary.Refused, Is.EqualTo(reader.Summary.Rows));
        }

        watch.Stop();
        TestContext.Out.WriteLine($"{ROUNDS} sets, {rows} rows in {watch.ElapsedMilliseconds} ms: {accepted} problems, {rows - accepted} refusals");
        foreach (var (code, count) in codes)
            TestContext.Out.WriteLine($"  {code}: {count}");

        // The fuzz is worth its name only while it reaches both outcomes, and every code of a row.
        Assert.That(accepted, Is.GreaterThan(rows / 10));
        Assert.That(rows - accepted, Is.GreaterThan(rows / 3));
        Assert.That(codes.Keys, Is.SupersetOf(new[]
        {
            SmtRefusalCodes.ROW_MALFORMED, SmtRefusalCodes.ID_INVALID, SmtRefusalCodes.ID_DUPLICATE, SmtRefusalCodes.FIELD_MISSING, SmtRefusalCodes.FIELD_INVALID,
            SmtRefusalCodes.FIELD_NOT_ALLOWED, SmtRefusalCodes.UNKNOWN_CONSTANT, SmtRefusalCodes.SYNTAX_ERROR, SmtRefusalCodes.COMMAND_NOT_ALLOWED, SmtRefusalCodes.NOT_A_TERM
        }));
    }

    #endregion

    #region Tools

    /// <summary>One to three lines, each a valid row changed once to three times.</summary>
    private static byte[] Set(Random random, string[] values)
    {
        var bytes = new List<byte>();
        var lines = 1 + random.Next(3);

        for (var line = 0; line < lines; line++)
        {
            var fields = ROWS[random.Next(ROWS.Length)].ToList();
            var changes = random.Next(4);
            for (var change = 0; change < changes; change++)
                Change(fields, random, values);

            var text = Encoding.UTF8.GetBytes("{" + string.Join(",", fields.Select(field => $"\"{field.Name}\":{field.Value}")) + "}").ToList();
            if (random.Next(6) == 0)
                Damage(text, random);

            bytes.AddRange(text);

            // Most lines end as lines do; some run into the next.
            bytes.AddRange(random.Next(12) switch
            {
                0 => [],
                1 or 2 => "\r\n"u8.ToArray(),
                _ => "\n"u8.ToArray()
            });
        }

        return bytes.ToArray();
    }

    private static void Change(List<(string Name, string Value)> fields, Random random, string[] values)
    {
        if (fields.Count == 0)
        {
            fields.Add((NAMES[random.Next(NAMES.Length)], values[random.Next(values.Length)]));
            return;
        }

        var at = random.Next(fields.Count);
        switch (random.Next(6))
        {
            case 0:
                fields.RemoveAt(at);
                break;
            case 1:
                fields.Insert(random.Next(fields.Count + 1), fields[at]);
                break;
            case 2:
                fields[at] = (fields[at].Name, values[random.Next(values.Length)]);
                break;
            case 3:
                fields[at] = (NAMES[random.Next(NAMES.Length)], fields[at].Value);
                break;
            case 4:
                fields.Insert(at, (NAMES[random.Next(NAMES.Length)], values[random.Next(values.Length)]));
                break;
            default:
                var other = random.Next(fields.Count);
                (fields[at], fields[other]) = (fields[other], fields[at]);
                break;
        }
    }

    /// <summary>A line cut short, or with raw bytes put in or over its own.</summary>
    private static void Damage(List<byte> line, Random random)
    {
        if (line.Count == 0)
            return;

        switch (random.Next(3))
        {
            case 0:
                var kept = random.Next(line.Count);
                line.RemoveRange(kept, line.Count - kept);
                break;
            case 1:
                line.Insert(random.Next(line.Count + 1), RAW[random.Next(RAW.Length)]);
                break;
            default:
                line[random.Next(line.Count)] = RAW[random.Next(RAW.Length)];
                break;
        }
    }

    /// <summary>
    /// Values of every JSON type, and of every kind a field takes or refuses: words of the
    /// queries and the verdicts, terms good and bad, names declared and not, numbers in and out
    /// of range, texts with escapes that are no text.
    /// </summary>
    private static string[] Values()
    {
        var half = "\\" + "ud83d";
        var nul = "\\" + "u0000";

        return
        [
            "null", "true", "false", "0", "1", "2", "5", "1000", "1001", "-1", "1.5", "2.0", "1e2", "1e400", "{}", "[]", "[1]", "[null]", "{\"id\":\"n\"}", "[[\"x\"]]",
            "\"\"", "\"a\"", "\"b\"", "\"a b\"", "\"x\"", "\"y\"", "\"z\"", "\"|x|\"", "\"x y\"", "\"(+ x 1)\"", "\"(> x\"", "\"oc!x\"", "\"-5\"", "\"|let|\"",
            "\"check\"", "\"enumerate\"", "\"prove\"", "\"solve\"", "\"sat\"", "\"unsat\"", "\"unknown\"",
            "[\"x\"]", "[\"y\",\"x\"]", "[\"x\",\"x\"]", "[\"z\"]", "[\"x\",5]", "[\"(+ x 1)\"]", "[\"\"]", "[\"x y\"]",
            $"\"{PROBLEM}\"", "\"(assert true)\"", "\"(push 1)\"", "\"(assert\"", "\"(declare-const x Int)(check-sat)(assert x)\"", "\"(set-option :a 1)\"", "\"\"",
            $"\"{half}\"", $"\"(assert{nul} true)\"", $"[\"{half}\"]", "\"a\\tb\"", "\"" + new string('t', 300) + "\"", "\"" + new string('i', 70) + "\""
        ];
    }

    #endregion
}
