using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// The problem of a row: the text is sanitized as any problem is, a refusal of the sanitizer
/// keeps its own code and says the line inside the text, and what is taken travels as the
/// problem's canonical text.
/// </summary>
[TestFixture]
public class SmtProblemSetProblemTests
{
    #region Accept Tests

    [Test]
    public void ProblemTravelsAsItsCanonicalTextWithItsHashTest()
    {
        var row = SmtProblemSets.Read("""{"id":"a","smt2":"; a comment\n(set-logic QF_LIA) (set-info :status sat)\n(declare-const |x| Int)\n(assert (> x\t0))\n(check-sat)\n(exit)\n"}""").Rows.Single();
        var plain = SmtSanitizer.Sanitize("(set-logic QF_LIA)(set-info :status sat)(declare-const x Int)(assert (> x 0))").Problem!;

        Assert.That(row.Data!.Text, Is.EqualTo("(set-logic QF_LIA)\n(set-info :status sat)\n(declare-const x Int)\n(assert (> x 0))\n"));
        Assert.That(row.Data.Hash, Is.EqualTo(plain.Hash), "the hash names the problem, however its row wrote it");
        Assert.That((row.Data.Id, row.Data.Query, row.Data.Expected, row.Data.Index), Is.EqualTo(("a", SmtQuery.Check, SmtProblemStatus.Sat, 0)));

        // Beside the problem that travels, the sanitizer's own: for a preview, for the constants.
        Assert.That(row.Problem!.Logic, Is.EqualTo("QF_LIA"));
        Assert.That(row.Problem.Constants.Select(constant => constant.Name), Is.EqualTo(new[] { "x" }));
        Assert.That(row.Problem.StrippedLines.Select(stripped => (stripped.Line, stripped.Command)), Is.EqualTo(new[] { (5, "check-sat"), (6, "exit") }));
    }

    [Test]
    public void ProblemWrittenWithJsonEscapesIsItsCharactersTest()
    {
        var escape = "\\" + "u";

        var data = SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{escape}0028assert (= s \\\"a\\\\b\\/c\\\")){escape}000a(assert true)\"}}");

        Assert.That(data.Text, Is.EqualTo("(assert (= s \"a\\b/c\"))\n(assert true)\n"));
    }

    [Test]
    public void ProblemWithoutCommandsIsAProblemTest()
    {
        // As for the sanitizer: a text of blanks and comments states nothing, and that is a problem.
        foreach (var text in new[] { "", "  ", "; nothing\\n" })
        {
            var data = SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{text}\"}}");

            Assert.That(data.Text, Is.Empty);
            Assert.That(data.Hash, Is.EqualTo("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
        }
    }

    #endregion

    #region Refusal Tests

    [TestCase("(declare-const x Int)\\n(push 1)\\n(assert x)", SmtRefusalCodes.COMMAND_NOT_ALLOWED, 2, "push")]
    [TestCase("(set-option :produce-proofs true)", SmtRefusalCodes.COMMAND_NOT_ALLOWED, 1, "set-option")]
    [TestCase("(declare-const x Int)\\n\\n(assert (> x -5))", SmtRefusalCodes.AMBIGUOUS_TOKEN, 3, "assert")]
    [TestCase("(declare-const |let| Int)", SmtRefusalCodes.RESERVED_SYMBOL, 1, "declare-const")]
    [TestCase("(declare-const oc!x Int)", SmtRefusalCodes.RESERVED_PREFIX, 1, "declare-const")]
    [TestCase("(assert true)\\n(check-sat)\\n(assert false)", SmtRefusalCodes.COMMAND_AFTER_END, 3, "assert")]
    [TestCase("(declare-const x)", SmtRefusalCodes.COMMAND_MALFORMED, 1, "declare-const")]
    [TestCase("(assert true)\\nx", SmtRefusalCodes.NOT_A_COMMAND, 2, "")]
    [TestCase("(assert (= x 1)", SmtRefusalCodes.SYNTAX_ERROR, 1, "")]
    [TestCase("(assert true) ; a note\\r(assert false)", SmtRefusalCodes.SYNTAX_ERROR, 1, "")]
    [TestCase("(declare-const v (_ BitVec 99999))", SmtRefusalCodes.INDEX_TOO_LARGE, 1, "declare-const")]
    public void ProblemTheSanitizerRefusesRefusesTheRowWithItsOwnCodeTest(string problem, string code, int line, string command)
    {
        var refusal = SmtProblemSets.Refuse($"\n\n{{\"id\":\"a\",\"smt2\":\"{problem}\"}}", code);

        Assert.That((refusal.Row, refusal.Line, refusal.Command, refusal.File), Is.EqualTo((3, line, command, "a")), "the row is the line of the set, the line is the line inside the problem");
        Assert.That(refusal.Reason, Is.EqualTo(SmtSanitizer.Sanitize(problem.Replace("\\n", "\n").Replace("\\r", "\r")).Refusal!.Reason), "the sanitizer's own words");
    }

    [Test]
    public void NulCharacterWrittenAsAnEscapeIsTheSanitizersToRefuseTest()
    {
        // The escape is valid JSON and a character of the text: the reader of SMT-LIB has no place for it.
        var nul = "\\" + "u0000";

        var refusal = SmtProblemSets.Refuse($"{{\"id\":\"a\",\"smt2\":\"(assert true)\\n(assert{nul} true)\"}}", SmtRefusalCodes.SYNTAX_ERROR);

        Assert.That((refusal.Row, refusal.Line, refusal.File), Is.EqualTo((1, 2, "a")));
        Assert.That(refusal.Reason, Is.EqualTo("the control character U+0000 outside a string literal and a quoted symbol"));
    }

    [Test]
    public void ProblemPastACeilingOfTheSanitizerRefusesTheRowTest()
    {
        // Four megabytes and one character of text in a line of less than sixteen: the row is read, the problem is too long.
        var text = new StringBuilder("{\"id\":\"a\",\"smt2\":\"(assert true)");
        text.Append(' ', SmtReaderLimits.PROBLEM_CHARACTERS - 12).Append("\"}\n{\"id\":\"b\",\"smt2\":\"(assert true)\"}");

        var rows = SmtProblemSets.Read(text.ToString()).Rows;

        Assert.That(rows[0].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.TEXT_TOO_LONG));
        Assert.That((rows[0].Refusal!.Row, rows[0].Refusal!.Line, rows[0].Refusal!.File), Is.EqualTo((1, 1, "a")));
        Assert.That(rows[1].IsAccepted, Is.True);

        Assert.That(SmtProblemSets.Refuse($"{{\"id\":\"a\",\"smt2\":\"(assert (> x {new string('7', 5_000)}))\"}}", SmtRefusalCodes.NUMERAL_TOO_LONG).Reason, Does.Not.Contain("77"));
    }

    [Test]
    public void RefusalOfARowNeverCarriesTheRowTest()
    {
        var secret = new string('q', 2_500) + new string('z', 2_500);
        var lines = new[]
        {
            $"{{\"id\":\"a\",\"smt2\":\"({secret} 1)\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert {secret}'x)\"}}",
            $"{{\"id\":\"{secret}\",\"smt2\":\"(assert true)\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"{secret}\":1}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"tags\":\"{secret}\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"query\":\"{secret}\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"values\":[\"-5{secret}\"]}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"query\":\"prove\",\"goal\":\"{secret} {secret}\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"query\":\"enumerate\",\"project\":[\"{secret}\"]}}",
            $"{{\"id\":\"a\",\"smt2\":\"(assert true)\",\"expected\":\"{secret}\"}}",
            $"{{\"id\":\"a\",\"smt2\":\"{secret}",
            $"{secret}"
        };

        foreach (var line in lines)
        {
            var refusal = SmtProblemSets.Read(line).Rows.Single().Refusal;

            Assert.That(refusal, Is.Not.Null, line[..60]);
            Assert.That(refusal!.Reason, Does.Not.Contain("qz"), line[..60]);
            Assert.That(refusal.Reason, Does.Not.Contain(new string('q', 65)), line[..60]);
            Assert.That(refusal.File + refusal.Command, Does.Not.Contain(new string('q', 65)), line[..60]);
        }
    }

    #endregion
}
