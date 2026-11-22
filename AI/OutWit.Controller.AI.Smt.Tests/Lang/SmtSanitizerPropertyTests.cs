using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The properties of the canonical form: it is a fixed point, the solver's text holds nothing
/// but what states a problem, the hash depends on the kept tokens and on nothing else, and a
/// refusal never carries the text. The hashes of three problems are written out here: a change
/// of the canonical form shows as a change of this file.
/// </summary>
[TestFixture]
public class SmtSanitizerPropertyTests
{
    #region Constants

    private const string LINEAR =
        "(set-info :smt-lib-version 2.6)\n(set-logic QF_LIA)\n(set-info :status sat)\n(declare-const x Int)\n(declare-fun y () Int)\n" +
        "(assert (and (> x 0) (< y 10) (= (+ x y) 7)))\n";

    private const string VECTORS =
        "(set-logic QF_BV)\n(declare-const a (_ BitVec 8))\n(declare-const |assert| (_ BitVec 8))\n" +
        "(define-fun twice ((v (_ BitVec 8))) (_ BitVec 8) (bvadd v v))\n(assert (! (= (twice a) #x10) :named double))\n" +
        "(assert (not (= |assert| #b00000001)))\n";

    private const string TREES =
        "(set-logic ALL)\n(set-info :source |a hand-written\nproblem|)\n" +
        "(declare-datatypes ((Tree 1)) ((par (T) ((leaf) (node (left (Tree T)) (value T) (right (Tree T)))))))\n" +
        "(declare-const |my tree| (Tree String))\n" +
        "(define-funs-rec ((even ((n Int)) Bool) (odd ((n Int)) Bool)) ((ite (= n 0) true (odd (- n 1))) (ite (= n 0) false (even (- n 1)))))\n" +
        "(assert (= (value |my tree|) \"say \"\"hi\"\"\"))\n(assert (even 4))\n";

    private const string LINEAR_HASH = "9bf0873fb9df5663c38ff47a482a7cbece2abb9bfa3b5b5c3c8594ce22f84b14";
    private const string VECTORS_HASH = "735b2b6182d6f16d162cc6b484decb255aa9d08cabdd48eb557ee3ce0fbbf879";
    private const string TREES_HASH = "109635c50ae52585d7dbaef05e03c49078065991754a2c6e09080c9cbdd13dc2";

    /// <summary>The first problem as somebody might have written it: comments, bars, blanks, and what asks the solver to act.</summary>
    private const string LINEAR_AS_WRITTEN =
        "; a small linear problem\r\n(set-info :smt-lib-version 2.6)\r\n(set-logic   |QF_LIA|) ; the logic\r\n(set-info :status sat)\r\n" +
        "(declare-const |x| Int)(declare-fun y ( ) Int)\r\n(push 0 ; not a command: a comment (exit)\r\n)\r\n";

    #endregion

    #region Pinned Hash Tests

    [TestCaseSource(nameof(Pinned))]
    public void HashOfAFixedProblemIsPinnedTest(string canonical, string hash)
    {
        // A change of the canonical form changes the name of every problem ever hashed. If this
        // fails, the form was changed: that is a decision, not a refactoring.
        var problem = SmtSanitizerChecks.Accept(canonical);

        Assert.That(problem.Canonical, Is.EqualTo(canonical));
        Assert.That(problem.Hash, Is.EqualTo(hash));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void HashIsTheSha256OfTheCanonicalTextInUtf8Test()
    {
        var problem = SmtSanitizerChecks.Accept("(declare-const |caf\u00e9 \ud83d\ude00| Int)");

        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(problem.Canonical)));

        Assert.That(problem.Canonical, Is.EqualTo("(declare-const |caf\u00e9 \ud83d\ude00| Int)\n"));
        Assert.That(problem.Hash, Is.EqualTo(expected));
        Assert.That(problem.Hash, Does.Match("^[0-9a-f]{64}$"));
    }

    #endregion

    #region Hash Independence Tests

    [Test]
    public void HashDoesNotDependOnCommentsTest()
    {
        var commented = "; first ( \" | a comment with everything\n" + LINEAR.Replace("\n", " ; (exit) \"open |open\n") + ";last, without a line end";

        AssertSameProblem(commented, LINEAR_HASH);
    }

    [Test]
    public void HashDoesNotDependOnBlanksAndLineEndingsTest()
    {
        var oneLine = LINEAR.Replace("\n", "");
        var spread = LINEAR.Replace(" ", "\n\t  ").Replace("(", " ( ").Replace(")", " ) ");
        var windows = LINEAR.Replace("\n", "\r\n");
        var tabs = LINEAR.Replace(' ', '\t');

        foreach (var text in new[] { oneLine, spread, windows, tabs, "\r\n\r\n" + windows + "\t \r\n" })
            AssertSameProblem(text, LINEAR_HASH);
    }

    [Test]
    public void HashDoesNotDependOnBarsAroundANameThatNeedsNoneTest()
    {
        var barred = LINEAR.Replace(" x ", " |x| ").Replace(" y ", " |y| ").Replace("QF_LIA", "|QF_LIA|").Replace("Int", "|Int|").Replace(" sat", " |sat|").Replace("(and", "(|and|").Replace("(>", "(|>|").Replace("(+", "(|+|");

        Assert.That(barred, Does.Contain("|x|").And.Contain("|and|").And.Contain("|Int|"));
        AssertSameProblem(barred, LINEAR_HASH);
    }

    [Test]
    public void HashDoesNotDependOnStrippedCommandsTest()
    {
        // Questions to the solver between the commands, and the check and the exit at the end.
        var withQueries = "(get-info :version)\n" + LINEAR.Replace("\n", "\n(get-model)\n(get-value (x y))\n(get-info :all-statistics)\n") + "(check-sat)\n(get-model)\n(exit)\n(check-sat)\n";

        var problem = AssertSameProblem(withQueries, LINEAR_HASH);
        Assert.That(problem.Stripped.Values.Sum(), Is.EqualTo(23));
    }

    [Test]
    public void ProblemAsWrittenIsTheProblemTest()
    {
        var refusal = SmtSanitizerChecks.Refuse(LINEAR_AS_WRITTEN, SmtRefusalCodes.COMMAND_NOT_ALLOWED);
        Assert.That((refusal.Command, refusal.Line), Is.EqualTo(("push", 6)));

        var written = LINEAR_AS_WRITTEN[..LINEAR_AS_WRITTEN.IndexOf("(push", StringComparison.Ordinal)] + "(assert (and (> x 0) (< y 10)\r\n\t(= (+ x y) 7)))\r\n(check-sat)\r\n";
        AssertSameProblem(written, LINEAR_HASH);
    }

    [Test]
    public void HashChangesWithEveryKeptTokenTest()
    {
        // Every token of the three problems, one at a time: replaced by another of its kind,
        // dropped, or written twice. Whatever is still a problem is another problem.
        foreach (var (text, hash) in new[] { (LINEAR, LINEAR_HASH), (VECTORS, VECTORS_HASH), (TREES.Replace("|a hand-written\nproblem|", "handwritten").Replace("|my tree|", "mytree").Replace("\"say \"\"hi\"\"\"", "\"hi\""), "") })
        {
            var original = hash.Length > 0 ? hash : SmtSanitizerChecks.Accept(text).Hash;
            var tokens = Tokens(text);
            var accepted = 0;

            for (var index = 0; index < tokens.Count; index++)
            {
                foreach (var changed in Changes(tokens, index))
                {
                    var result = SmtSanitizer.Sanitize(string.Join(' ', changed));
                    if (result.Problem == null)
                        continue;

                    accepted++;
                    Assert.That(result.Problem.Hash, Is.Not.EqualTo(original), () => string.Join(' ', changed));
                }
            }

            Assert.That(accepted, Is.GreaterThan(tokens.Count / 2), "many changes of one token leave a problem: the test must have seen them");
        }
    }

    [Test]
    public void HashDoesNotDependOnHowAFileEndsTheLinesOfItsInformationTest()
    {
        // Nearly every problem of a public collection carries a source of several lines: the
        // file with line feeds and the file with carriage returns are the same problem.
        AssertSameProblem(TREES.Replace("\n", "\r\n"), TREES_HASH);

        var unix = SmtSanitizerChecks.Accept("(set-info :source |two\nlines|)\n(set-info :license \"a\nb\")\n(set-info :note (deep (\"c\nd\" |e\nf|)))");
        var windows = SmtSanitizerChecks.Accept("(set-info :source |two\r\nlines|)\r\n(set-info :license \"a\r\nb\")\r\n(set-info :note (deep (\"c\r\r\nd\" |e\r\nf|)))");

        Assert.That(windows.Canonical, Is.EqualTo("(set-info :source |two\nlines|)\n(set-info :license \"a\nb\")\n(set-info :note (deep (\"c\nd\" |e\nf|)))\n"));
        Assert.That(windows.Hash, Is.EqualTo(unix.Hash));
        Assert.That(windows.Source, Is.EqualTo("two\nlines"));
        Assert.That(windows.License, Is.EqualTo("a\nb"));
        Assert.That(windows.Commands.Select(command => command.Line), Is.EqualTo(new[] { 1, 3, 5 }));
        SmtSanitizerChecks.HoldProperties(windows);
    }

    [Test]
    public void CarriageReturnThatEndsNoLineIsKeptInInformationTest()
    {
        var problem = SmtSanitizerChecks.Accept("(set-info :source |a\rb\r|)\n(set-info :note \"\r\")");

        Assert.That(problem.Canonical, Is.EqualTo("(set-info :source |a\rb\r|)\n(set-info :note \"\r\")\n"));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [Test]
    public void LineBreakInsideALiteralOfTheSolversCommandsIsContentTest()
    {
        // Outside set-info a line break inside a string or a quoted symbol is part of a value or
        // of a name: two texts that differ there are two problems, by the standard as well.
        var unix = SmtSanitizerChecks.Accept("(declare-const |two\nlines| String)\n(assert (= |two\nlines| \"a\nb\"))");
        var windows = SmtSanitizerChecks.Accept("(declare-const |two\r\nlines| String)\r\n(assert (= |two\r\nlines| \"a\r\nb\"))");

        Assert.That(windows.Hash, Is.Not.EqualTo(unix.Hash));
        Assert.That(windows.Canonical, Is.EqualTo("(declare-const |two\r\nlines| String)\n(assert (= |two\r\nlines| \"a\r\nb\"))\n"));
        Assert.That(windows.SolverText, Is.EqualTo(windows.Canonical));
        SmtSanitizerChecks.HoldProperties(unix);
        SmtSanitizerChecks.HoldProperties(windows);
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void RefusalNeverCarriesTheTextTest()
    {
        var secret = new string('q', 2_500) + new string('z', 2_500);
        var digits = new string('9', 4_000);
        var texts = new[]
        {
            $"({secret} 1)",
            $"(|{secret}| 1)",
            $"(|{secret} {secret}|)",
            $"{secret}",
            $"({secret}",
            $"(assert {secret}'x)",
            $"(assert \"{secret}",
            $"(assert |{secret}",
            $"(assert \"{secret}\u00e9\")",
            $"(assert -5{secret})",
            $"(assert |-5{secret}|)",
            $"(assert (_ {secret} 9{digits}))",
            $"(declare-const oc!{secret} Int)",
            $"(declare-const {secret})",
            $"(declare-const {secret} Int Int)",
            $"(declare-fun {secret} {secret} {secret})",
            $"(declare-datatype {secret} ({secret}))",
            $"(set-info {secret})",
            $"(set-info :status {secret} {secret})",
            $"(set-option :{secret} {secret})",
            $"(echo \"{secret}\")",
            $"(assert {secret}) (push {secret})",
            $"(assert (|let| {secret}))",
            $"(assert 9{digits}9{digits})"
        };

        foreach (var text in texts)
        {
            var result = SmtSanitizer.Sanitize(text);

            Assert.That(result.Problem, Is.Null, text[..40]);
            SmtSanitizerChecks.HoldRefusal(result.Refusal!);
            Assert.That(result.Refusal!.Reason, Does.Not.Contain(new string('q', 65)), text[..40]);
            Assert.That(result.Refusal.Reason, Does.Not.Contain("qz"), text[..40]);
            Assert.That(result.Refusal.Reason, Does.Not.Contain(new string('9', 65)), text[..40]);
            Assert.That(result.Refusal.Command, Does.Not.Contain("qz"), text[..40]);
        }
    }

    #endregion

    #region Tools

    private static IEnumerable<TestCaseData> Pinned()
    {
        yield return new TestCaseData(LINEAR, LINEAR_HASH).SetArgDisplayNames("linear");
        yield return new TestCaseData(VECTORS, VECTORS_HASH).SetArgDisplayNames("vectors");
        yield return new TestCaseData(TREES, TREES_HASH).SetArgDisplayNames("trees");
    }

    private static SmtProblem AssertSameProblem(string text, string hash)
    {
        var problem = SmtSanitizerChecks.Accept(text);

        Assert.That(problem.Hash, Is.EqualTo(hash));
        SmtSanitizerChecks.HoldProperties(problem);
        return problem;
    }

    /// <summary>The tokens of a text that holds no blank inside a token.</summary>
    private static List<string> Tokens(string text)
    {
        return text.Replace("(", " ( ").Replace(")", " ) ").Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static IEnumerable<List<string>> Changes(List<string> tokens, int index)
    {
        var token = tokens[index];
        if (token is not ("(" or ")"))
        {
            var other = new List<string>(tokens);
            other[index] = Other(token);
            yield return other;
        }

        var dropped = new List<string>(tokens);
        dropped.RemoveAt(index);
        yield return dropped;

        var doubled = new List<string>(tokens);
        doubled.Insert(index, token);
        yield return doubled;
    }

    /// <summary>Another token of the same kind.</summary>
    private static string Other(string token)
    {
        if (char.IsAsciiDigit(token[0]))
            return token + "1";

        if (token[0] == '#')
            return token + "0";

        if (token[0] == '"')
            return token[..^1] + "!\"";

        return token[0] == '|' ? token[..^1] + "2|" : token + "2";
    }

    #endregion
}
