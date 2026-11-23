using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

[TestFixture]
public sealed class SmtLivePreflightTests
{
    #region Fields

    private string m_root = null!;
    private string m_set = null!;
    private SmtLiveServerFake m_server = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_set = Path.Combine(m_root, "problems.jsonl");
        File.WriteAllText(m_set, "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n");
        m_server = new SmtLiveServerFake();
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_root);
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task ARunnablePreflightIsPrintedAndExitsZeroTest()
    {
        Answer(Preflight());
        var options = new SmtOptionsData { HardCapSeconds = 100, SamplingRate = 1 };

        var exit = await new SmtLivePreflight(m_server, m_output).RunAsync(m_set, options, CancellationToken.None);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(m_server.Submitted.Single().Script, Is.EqualTo("SmtPreflight"));
            Assert.That(m_server.Submitted.Single().Parameters[0], Is.EqualTo(m_server.Uploaded.Single()));
            Assert.That(((SmtOptionsData)m_server.Submitted.Single().Parameters[1]!).Is(options), Is.True);
            Assert.That(output, Does.Contain("runnable  : yes"));
            Assert.That(output, Does.Contain("rows      : 3: 2 taken, 1 refused"));
            Assert.That(output, Does.Contain("refused   : syntax_error 1"));
            Assert.That(output, Does.Contain("row 3 (bad), line 2 (assert): syntax_error: the text ends inside a list"));
            Assert.That(output, Does.Contain("logics    : QF_LIA 1, (none) 1"));
            Assert.That(output, Does.Contain("the options are taken"));
            Assert.That(output, Does.Contain("a node needs 2752 MiB available for one session"));
            Assert.That(output, Does.Contain("bound     : 200 s of the reference machine"));
            Assert.That(output, Does.Contain("packages  : 1, at most 16 problem(s) each"));
        });
    }

    [Test]
    public async Task RefusedOptionsAreSaidLoudlyAndExitOneTest()
    {
        var preflight = Preflight();
        preflight.OptionProblems = ["'seed' must be between 0 and 2147483647"];
        Answer(preflight);

        var exit = await new SmtLivePreflight(m_server, m_output).RunAsync(m_set, new SmtOptionsData { Seed = -1 }, CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("!!! checked   : THE OPTIONS ARE REFUSED").And.Contain("'seed' must be between 0 and 2147483647"));
    }

    [Test]
    public async Task APreflightThatWouldNotStartExitsOneTest()
    {
        var preflight = Preflight();
        preflight.Runnable = false;
        Answer(preflight);

        var exit = await new SmtLivePreflight(m_server, m_output).RunAsync(m_set, new SmtOptionsData(), CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("!!! runnable  : NO"));
    }

    [Test]
    public async Task APreflightWithoutAnAnswerExitsOneTest()
    {
        Answer(null);

        var exit = await new SmtLivePreflight(m_server, m_output).RunAsync(m_set, new SmtOptionsData(), CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("no preflight came back"));
    }

    #endregion

    #region Tools

    private void Answer(SmtPreflightData? preflight)
    {
        m_server.Jobs = (_, _) => SmtLiveJobFake.Completed(new Dictionary<string, object?> { ["result"] = preflight });
    }

    private static SmtPreflightData Preflight()
    {
        return new SmtPreflightData
        {
            Runnable = true,
            Rows = 3,
            Accepted = 2,
            Refused = 1,
            BytesRead = 321,
            Sha256 = new string('5', 64),
            RefusalCodes = [SmtCountData.Of("syntax_error", 1)],
            Refusals = [new SmtRefusalData { Row = 3, File = "bad", Line = 2, Command = "assert", Code = "syntax_error", Reason = "the text ends inside a list" }],
            Logics = [SmtCountData.Of("QF_LIA", 1), SmtCountData.Of("", 1)],
            Queries = [SmtCountData.Of("check", 2)],
            Expected = [SmtCountData.Of("sat", 1), SmtCountData.Of("none", 1)],
            Stripped = [SmtCountData.Of("check-sat", 2)],
            Profile = "z3-5.4.1",
            ResourceLimit = 150_000_000,
            HardCapSeconds = 100,
            Fuel = 500_000_000_000,
            MemoryMegabytes = 1024,
            MaxModels = 2,
            SamplingRate = 1,
            SessionMemoryBytes = 2752L * 1024 * 1024,
            UpperBoundSeconds = 200,
            Packages = 1,
            ProblemsPerPackage = 16
        };
    }

    #endregion
}
