using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests;

/// <summary>
/// The tool as a whole, from its command line: what is refused before any connection is made.
/// The environment's variables are cleared for each test, so no test can reach a server.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtLiveProgramTests
{
    #region Fields

    private string m_root = null!;
    private string m_set = null!;
    private Dictionary<string, string?> m_saved = null!;
    private TextWriter m_out = null!;
    private TextWriter m_error = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_set = Path.Combine(m_root, "problems.jsonl");
        File.WriteAllText(m_set, "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n");

        m_saved = Variables().ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in Variables())
            Environment.SetEnvironmentVariable(name, null);

        m_out = Console.Out;
        m_error = Console.Error;
        m_output = new StringWriter();
        Console.SetOut(m_output);
        Console.SetError(m_output);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetOut(m_out);
        Console.SetError(m_error);
        foreach (var (name, value) in m_saved)
            Environment.SetEnvironmentVariable(name, value);

        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_root);
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task OptionsOutOfRangeAreRefusedBeforeTheEnvironmentIsReadTest()
    {
        var exit = await Program.Main(["run", m_set, "--rate", "2", "--out", Path.Combine(m_root, "run")]);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
            Assert.That(m_output.ToString(), Does.Contain("'sampling_rate' must be between 0 and 1"));
            Assert.That(m_output.ToString(), Does.Not.Contain("not set:"), "the options are refused first, with no connection asked for");
        });
    }

    [Test]
    public async Task AFolderThatCannotBeUsedIsRefusedBeforeTheEnvironmentIsReadTest()
    {
        var folder = Path.Combine(m_root, "run");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "results.jsonl"), "");

        var exit = await Program.Main(["run", m_set, "--out", folder]);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("already holds results.jsonl").And.Not.Contain("not set:"));
    }

    [Test]
    public async Task AnAddressThatIsNotHttpsIsRefusedBeforeAConnectionTest()
    {
        Environment.SetEnvironmentVariable(SmtLiveEnvironment.SERVER_VARIABLE, "http://server.invalid");
        Environment.SetEnvironmentVariable(SmtLiveEnvironment.IDENTITY_VARIABLE, "https://identity.invalid");
        Environment.SetEnvironmentVariable(SmtLiveEnvironment.KEY_VARIABLE, "wit_sk_not_a_real_key");

        var exit = await Program.Main(["diagnostics"]);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain(SmtLiveEnvironment.SERVER_VARIABLE).And.Not.Contain("server   :"), "refused before the tool says where it connects");
    }

    [Test]
    public async Task FetchTakesAJobIdAndNothingElseTest()
    {
        var exit = await Program.Main(["fetch", "not-a-job"]);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("'not-a-job' is not a job id"));
    }

    #endregion

    #region Tools

    private static string[] Variables()
    {
        return [SmtLiveEnvironment.SERVER_VARIABLE, SmtLiveEnvironment.IDENTITY_VARIABLE, SmtLiveEnvironment.KEY_VARIABLE];
    }

    #endregion
}
