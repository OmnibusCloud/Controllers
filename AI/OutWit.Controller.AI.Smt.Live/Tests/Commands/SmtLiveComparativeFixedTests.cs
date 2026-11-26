using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// What the crossover of <c>comparative</c> is drawn from: two runs that ran the same thing and
/// agree row by row, and a <c>--fixed</c> run that ran the fleet run's module with its options
/// and its sampling rate and completed; and a measurement is read only beside the documents of
/// the set it measured. The fixtures are those of the comparative tests.
/// </summary>
[TestFixture]
public sealed class SmtLiveComparativeFixedTests
{
    #region Constants

    private const string SMALL_SET = "8fcb09369a7fc3a9ed2d59eac093769fdd52d0d66359de1cf83244dca707fd08";
    private const string NO_CROSSOVER = "crossover : not computed: ";

    #endregion

    #region Fields

    private string m_root = null!;
    private string m_local = null!;
    private string m_fleet = null!;
    private string m_small = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_local = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "local"));
        m_fleet = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "fleet"));
        m_small = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "small"));
        File.Copy(Measured("local.json"), Path.Combine(m_local, SmtLiveLocalFacts.FILE));
        File.Copy(Measured("fleet.json"), Path.Combine(m_fleet, SmtLiveFleetFacts.FILE));
        File.Copy(Measured("fleet-small.json"), Path.Combine(m_small, SmtLiveFleetFacts.FILE));
        SmtLiveFixtures.SetOfRun(m_small, SMALL_SET, 3342, 2);
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

    #region Sameness Tests

    [Test]
    public void TheFixturesGiveACrossoverTest()
    {
        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
        Assert.That(m_output.ToString(), Does.Contain("crossover : the fleet finishes first above 22 problems"));
    }

    [Test]
    public void RunsWhoseRowsDifferGiveNoCrossoverTest()
    {
        SmtLiveFixtures.EditRow(Path.Combine(m_fleet, SmtLiveDocuments.RESULTS.File), 1, row => row["digest"] = new string('e', 64));

        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.DIFFER));
        Assert.That(m_output.ToString(), Does.Contain(NO_CROSSOVER + "the two runs' rows differ").And.Not.Contain("finishes first above"));
    }

    #endregion

    #region Fixed Run Tests

    [Test]
    public void AFixedRunOfOtherOptionsGivesNoCrossoverTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_small, SmtLiveDocuments.PROVENANCE.File), root => root["options"]!["hard_cap_s"] = 300);

        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK), "the two compared runs are the same; the crossover is information");
        Assert.That(m_output.ToString(), Does.Contain(NO_CROSSOVER + "the --fixed run differs from the fleet run").And.Contain("options.hard_cap_s"));
    }

    [Test]
    public void AFixedRunAtAnotherSamplingRateGivesNoCrossoverTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_small, SmtLiveDocuments.PROVENANCE.File), root =>
        {
            root["options"]!["sampling_rate"] = 0.05;
            root["sampling_rate"] = 0.05;
        });

        Run();

        Assert.That(m_output.ToString(), Does.Contain(NO_CROSSOVER + "the --fixed run differs from the fleet run").And.Contain("sampling_rate"));
    }

    [Test]
    public void AFixedRunOfAnotherModuleGivesNoCrossoverTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_small, SmtLiveDocuments.PROVENANCE.File), root => root["profile"]!["module_sha256"] = new string('7', 64));

        Run();

        Assert.That(m_output.ToString(), Does.Contain(NO_CROSSOVER + "the --fixed run differs from the fleet run").And.Contain("module"));
    }

    [Test]
    public void AFixedRunThatDidNotCompleteGivesNoCrossoverTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_small, SmtLiveFleetFacts.FILE), root => root["status"] = "Failed");

        Run();

        Assert.That(m_output.ToString(), Does.Contain(NO_CROSSOVER + "the --fixed run did not complete (Failed)"));
    }

    [Test]
    public void AFixedFolderWithoutItsDocumentsIsRefusedTest()
    {
        File.Delete(Path.Combine(m_small, SmtLiveDocuments.PROVENANCE.File));

        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("provenance.json is not there"));
    }

    #endregion

    #region Measurement Tests

    [Test]
    public void AMeasurementOfAnotherSetThanItsFolderIsRefusedTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_fleet, SmtLiveFleetFacts.FILE), root => root["problem_set"]!["sha256"] = new string('9', 64));

        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("fleet.json measured the set 9999").And.Contain("its provenance names the set 5555"));
    }

    [Test]
    public void ALocalMeasurementOfAnotherSetThanItsFolderIsRefusedTest()
    {
        SmtLiveFixtures.Edit(Path.Combine(m_local, SmtLiveLocalFacts.FILE), root => root["problem_set"]!["sha256"] = new string('9', 64));

        var exit = Run();

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("local.json measured the set 9999"));
    }

    #endregion

    #region Tools

    private int Run()
    {
        return new SmtLiveComparative(m_output).Run(m_local, m_fleet, m_small);
    }

    private static string Measured(string file)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "measured", file);
    }

    #endregion
}
