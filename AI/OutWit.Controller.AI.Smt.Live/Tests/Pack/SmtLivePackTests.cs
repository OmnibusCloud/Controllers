using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Pack;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Tests.Pack;

[TestFixture]
public sealed class SmtLivePackTests
{
    #region Constants

    private const string SAT = "(set-info :status sat)\n(declare-const x Int)\n(assert (> x 1))\n(check-sat)\n";
    private const string UNSAT = "; a comment\r\n(set-logic QF_LIA)\r\n(set-info :status unsat)\r\n(declare-const x Int)\r\n(assert (> x 1))\r\n(assert (< x 0))\r\n(check-sat)\r\n(exit)\r\n";

    #endregion

    #region Fields

    private string m_folder = null!;
    private string m_set = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var root = SmtLiveFixtures.NewFolder();
        m_folder = Path.Combine(root, "problems");
        m_set = Path.Combine(root, "out", "problems.jsonl");
        Directory.CreateDirectory(m_folder);
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(Path.GetDirectoryName(m_folder)!);
        m_output.Dispose();
    }

    #endregion

    #region Check Tests

    [Test]
    public void PackWritesOneRowAFileInTheOrderOfTheirPathsTest()
    {
        Write("b.smt2", UNSAT);
        Write("a.smt2", SAT);
        Write("sub/c.smt2", SAT);
        Write("notes.txt", "not a problem");

        var exit = new SmtLivePack(m_output).Run(m_folder, null, m_set);

        var rows = Rows();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(rows.Select(row => row.GetProperty("id").GetString()), Is.EqualTo(new[] { "a", "b", "sub/c" }));
            Assert.That(rows.All(row => !row.TryGetProperty("query", out _) && !row.TryGetProperty("expected", out _)), Is.True, "no query, no expected: the run's query, the problem's :status");
            Assert.That(rows[1].GetProperty("smt2").GetString(), Is.EqualTo(UNSAT), "the text as the file has it, comments and line ends included");
            Assert.That(m_output.ToString(), Does.Contain("3 taken, 0 refused").And.Contain("sat 2, unsat 1"));
        });

        var summary = Read(null);
        Assert.That(summary.Accepted, Is.EqualTo(3));
    }

    [Test]
    public void PackPrintsTheRowsTheServerWouldRefuseAndStillWritesThemTest()
    {
        Write("good.smt2", SAT);
        Write("push.smt2", "(declare-const x Int)\n(push 1)\n(assert (> x 1))\n");
        Write("broken.smt2", "(declare-const x Int)\n(assert (> x 1)\n");

        var exit = new SmtLivePack(m_output).Run(m_folder, null, m_set);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
            Assert.That(Rows(), Has.Count.EqualTo(3), "a refused row is still a row of the set: its results say why");
            Assert.That(output, Does.Contain("(push): command_not_allowed"));
            Assert.That(output, Does.Contain("(broken)").And.Contain("syntax_error"));
            Assert.That(output, Does.Contain("1 taken, 2 refused"));
        });
    }

    [Test]
    public void PackSkipsAFileThatIsNotUtf8Test()
    {
        Write("good.smt2", SAT);
        File.WriteAllBytes(Path.Combine(m_folder, "latin1.smt2"), [0x28, 0x61, 0xE9, 0x29]);

        var exit = new SmtLivePack(m_output).Run(m_folder, null, m_set);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(Rows(), Has.Count.EqualTo(1));
        Assert.That(m_output.ToString(), Does.Contain("skipped  : latin1.smt2: not valid UTF-8"));
    }

    [Test]
    public void PackOfAFolderWithoutProblemsIsAUsageErrorTest()
    {
        Write("notes.txt", "nothing");

        Assert.That(new SmtLivePack(m_output).Run(m_folder, null, m_set), Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(new SmtLivePack(m_output).Run(Path.Combine(m_folder, "missing"), null, m_set), Is.EqualTo(SmtLiveExitCodes.USAGE));
    }

    [Test]
    public void PackGivesTheSameBytesEveryTimeTest()
    {
        Write("z.smt2", SAT);
        Write("a/b.smt2", UNSAT);
        Write("a b.smt2", SAT);

        new SmtLivePack(m_output).Run(m_folder, SmtQuery.Enumerate, m_set);
        var first = File.ReadAllBytes(m_set);
        new SmtLivePack(m_output).Run(m_folder, SmtQuery.Enumerate, m_set);

        Assert.That(File.ReadAllBytes(m_set), Is.EqualTo(first));
        Assert.That(first[^1], Is.EqualTo((byte)'\n'));
        Assert.That(first.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "no byte order mark");
    }

    #endregion

    #region Query Tests

    [Test]
    public void PackWithACheckQueryNamesItInEveryRowTest()
    {
        Write("a.smt2", SAT);

        var exit = new SmtLivePack(m_output).Run(m_folder, SmtQuery.Check, m_set);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
        Assert.That(Rows()[0].GetProperty("query").GetString(), Is.EqualTo("check"));
    }

    [Test]
    public void PackWithEnumerateProjectsOnEveryConstantTheProblemDeclaresTest()
    {
        Write("e.smt2", "(declare-const x Int)\n(declare-fun |odd name| () Int)\n(declare-fun f (Int) Int)\n(declare-fun b () Bool)\n(assert (= (f x) |odd name|))\n");

        var exit = new SmtLivePack(m_output).Run(m_folder, SmtQuery.Enumerate, m_set);

        var row = Rows()[0];
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK), m_output.ToString());
            Assert.That(row.GetProperty("query").GetString(), Is.EqualTo("enumerate"));
            Assert.That(row.GetProperty("project").EnumerateArray().Select(name => name.GetString()), Is.EqualTo(new[] { "x", "|odd name|", "b" }));
            Assert.That(row.TryGetProperty("max_models", out _), Is.False, "the number of models is the run's");
        });
    }

    [Test]
    public void PackWithEnumerateSaysWhyAProblemWithoutConstantsIsRefusedTest()
    {
        Write("f.smt2", "(declare-fun f (Int) Int)\n(assert (= (f 1) 2))\n");

        var exit = new SmtLivePack(m_output).Run(m_folder, SmtQuery.Enumerate, m_set);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("field_missing").And.Contain("no constant to project on"));
    }

    [Test]
    public void PackWithProveTakesTheLastAssertAsTheGoalTest()
    {
        Write("vc.smt2", "(set-logic QF_NIA)\n(declare-const x Int)\n(assert (> x 1))\n; the goal\n(assert (> (* x x) x))\n(check-sat)\n");

        var exit = new SmtLivePack(m_output).Run(m_folder, SmtQuery.Prove, m_set);

        var row = Rows()[0];
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK), m_output.ToString());
            Assert.That(row.GetProperty("query").GetString(), Is.EqualTo("prove"));
            Assert.That(row.GetProperty("goal").GetString(), Is.EqualTo("(> (* x x) x)"));
            Assert.That(row.GetProperty("smt2").GetString(), Is.EqualTo("(set-logic QF_NIA)\n(declare-const x Int)\n(assert (> x 1))\n"));
        });
    }

    [Test]
    public void PackWithProveSaysWhyAProblemWithoutAnAssertIsRefusedTest()
    {
        Write("empty.smt2", "(declare-const x Int)\n");
        Write("broken.smt2", "(assert");

        var exit = new SmtLivePack(m_output).Run(m_folder, SmtQuery.Prove, m_set);

        var output = m_output.ToString();
        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(output, Does.Contain("no assert to take as the goal"));
        Assert.That(output, Does.Contain("the sanitizer refuses the problem: syntax_error"));
        Assert.That(output, Does.Contain("0 taken, 2 refused"));
    }

    #endregion

    #region Tools

    private void Write(string relative, string text)
    {
        var path = Path.Combine(m_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private List<JsonElement> Rows()
    {
        return File.ReadAllLines(m_set).Where(line => line.Length > 0).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
    }

    private SmtProblemSetSummary Read(SmtQuery? query)
    {
        using var stream = File.OpenRead(m_set);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData { Query = query ?? SmtQuery.Check });
        while (reader.TryRead(out _))
        {
        }

        return reader.Summary;
    }

    #endregion
}
