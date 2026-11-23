using System.Text;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The adversarial corpus: <c>Fixtures/Adversarial/*.smt2</c>, one idea a file, bytes exact. A
/// file starts with comment lines that say how it must end:
/// <list type="bullet">
/// <item><c>; expect: accepted</c>, or <c>; expect:</c> and a refusal code;</item>
/// <item><c>; idea:</c> what the file tries;</item>
/// <item>for a refusal, <c>; line:</c> and <c>; command:</c>;</item>
/// <item>for a problem, <c>; commands:</c> (how many are kept), <c>; canonical-has:</c>,
/// <c>; canonical-lacks:</c>, <c>; solver-has:</c>, <c>; solver-lacks:</c>, <c>; status:</c>,
/// <c>; stripped:</c> (<c>none</c>, or <c>name=count</c> a line) and <c>; hash:</c>.</item>
/// </list>
/// A file is read as UTF-8 without any repair: a byte order mark stays a character, a byte that
/// is no UTF-8 becomes U+FFFD.
/// </summary>
[TestFixture]
public class SmtSanitizerAdversarialTests
{
    #region Constants

    private const string FOLDER = "Fixtures/Adversarial";
    private const string ACCEPTED = "accepted";
    private const int MIN_FILES = 60;
    private const char BYTE_ORDER_MARK = (char)0xFEFF;

    private static readonly string[] KEYS =
    [
        "expect", "idea", "line", "command", "commands", "canonical-has", "canonical-lacks", "solver-has", "solver-lacks", "status", "stripped", "hash"
    ];

    private static readonly Regex HEADER_LINE = new("^; ([a-z-]+): (.*)$", RegexOptions.CultureInvariant);

    #endregion

    #region Corpus Tests

    [TestCaseSource(nameof(Files))]
    public void AdversarialFileEndsAsItSaysTest(string name)
    {
        var text = Read(name);
        var header = Header(text);
        var expect = header.Single(line => line.Key == "expect").Value;

        Assert.That(header.Count(line => line.Key == "idea" && line.Value.Length > 0), Is.EqualTo(1), "a file says what it tries");

        var result = SmtSanitizer.Sanitize(text);
        if (expect == ACCEPTED)
        {
            Assert.That(result.Refusal, Is.Null, () => $"refused: {result.Refusal?.Code}, line {result.Refusal?.Line}: {result.Refusal?.Reason}");
            HoldProblem(result.Problem!, header);
            return;
        }

        Assert.That(SmtRefusalCodes.All, Does.Contain(expect), "the file expects a code that does not exist");
        Assert.That(result.Problem, Is.Null, "the file was taken");
        Assert.That(result.Refusal!.Code, Is.EqualTo(expect), result.Refusal.Reason);
        SmtSanitizerChecks.HoldRefusal(result.Refusal);

        foreach (var (key, value) in header)
        {
            switch (key)
            {
                case "line":
                    Assert.That(result.Refusal.Line, Is.EqualTo(int.Parse(value)));
                    break;
                case "command":
                    Assert.That(result.Refusal.Command, Is.EqualTo(value));
                    break;
                case "expect" or "idea":
                    break;
                default:
                    Assert.Fail($"'{key}' says something of a problem, and the file expects a refusal");
                    break;
            }
        }
    }

    [Test]
    public void AdversarialCorpusIsThereAndReachesEveryRuleAFileCanReachTest()
    {
        var expectations = Files().Select(name => Header(Read(name)).Single(line => line.Key == "expect").Value).ToList();

        Assert.That(expectations, Has.Count.GreaterThanOrEqualTo(MIN_FILES));
        Assert.That(expectations.Count(expect => expect == ACCEPTED), Is.GreaterThanOrEqualTo(20), "what must survive is part of the corpus");

        // The other codes need a text of megabytes: SmtSanitizerRefusalTests makes those.
        Assert.That(expectations, Is.SupersetOf(new[]
        {
            SmtRefusalCodes.SYNTAX_ERROR, SmtRefusalCodes.NUMERAL_TOO_LONG, SmtRefusalCodes.NOT_A_COMMAND,
            SmtRefusalCodes.COMMAND_NOT_ALLOWED, SmtRefusalCodes.COMMAND_AFTER_END, SmtRefusalCodes.COMMAND_MALFORMED, SmtRefusalCodes.RESERVED_SYMBOL, SmtRefusalCodes.AMBIGUOUS_TOKEN,
            SmtRefusalCodes.NON_ASCII_STRING, SmtRefusalCodes.INDEX_TOO_LARGE, SmtRefusalCodes.RESERVED_PREFIX, SmtRefusalCodes.BUILTIN_NAME
        }));
    }

    #endregion

    #region Tools

    private static void HoldProblem(SmtProblem problem, List<(string Key, string Value)> header)
    {
        SmtSanitizerChecks.HoldProperties(problem);

        var stripped = header.Where(line => line.Key == "stripped").Select(line => line.Value).ToList();
        if (stripped.Count > 0)
        {
            var expected = stripped.Where(value => value != "none").Select(value => value.Split('=')).ToDictionary(pair => pair[0], pair => int.Parse(pair[1]));
            Assert.That(problem.Stripped, Is.EquivalentTo(expected));
        }

        foreach (var (key, value) in header)
        {
            switch (key)
            {
                case "commands":
                    Assert.That(problem.Commands, Has.Count.EqualTo(int.Parse(value)));
                    break;
                case "canonical-has":
                    Assert.That(problem.Canonical, Does.Contain(value));
                    break;
                case "canonical-lacks":
                    Assert.That(problem.Canonical, Does.Not.Contain(value));
                    break;
                case "solver-has":
                    Assert.That(problem.SolverText, Does.Contain(value));
                    break;
                case "solver-lacks":
                    Assert.That(problem.SolverText, Does.Not.Contain(value));
                    break;
                case "status":
                    Assert.That(problem.Status.ToString().ToLowerInvariant(), Is.EqualTo(value));
                    break;
                case "hash":
                    Assert.That(problem.Hash, Is.EqualTo(value));
                    break;
                case "line" or "command":
                    Assert.Fail($"'{key}' says something of a refusal, and the file expects a problem");
                    break;
            }
        }
    }

    /// <summary>The comment lines a file starts with, as keys and values; the first is the expectation.</summary>
    private static List<(string Key, string Value)> Header(string text)
    {
        var header = new List<(string Key, string Value)>();
        foreach (var line in text.TrimStart(BYTE_ORDER_MARK).Split('\n'))
        {
            var match = HEADER_LINE.Match(line.TrimEnd('\r'));
            if (!match.Success || !KEYS.Contains(match.Groups[1].Value))
                break;

            header.Add((match.Groups[1].Value, match.Groups[2].Value));
        }

        Assert.That(header, Is.Not.Empty, "the file does not say how it must end");
        Assert.That(header[0].Key, Is.EqualTo("expect"), "the first line of a file is its expectation");
        return header;
    }

    private static string Read(string name)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, FOLDER, name + ".smt2");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(File.ReadAllBytes(path));
    }

    private static IEnumerable<string> Files()
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, FOLDER);
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetFiles(folder, "*.smt2").Select(path => Path.GetFileNameWithoutExtension(path)!).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    #endregion
}
