using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// <c>comparative</c> on fixtures: the four documents of the fixture run on both sides, with the
/// measurement of a local run (start-up 20 s, 23 s in all for 3 problems) and of a fleet run
/// (40 s for the same 3), and a small fleet run (1 problem in 39.8 s) for the fixed cost.
/// </summary>
[TestFixture]
public sealed class SmtLiveComparativeTests
{
    #region Constants

    /// <summary>The set of the small fleet run, as its measurement names it.</summary>
    private const string SMALL_SET = "8fcb09369a7fc3a9ed2d59eac093769fdd52d0d66359de1cf83244dca707fd08";

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

    #region Tests

    [Test]
    public void TwoRunsOfTheSameThingGiveTheTableTheRowsAndTheCrossoverTest()
    {
        var exit = Run(m_small);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(output, Does.Contain("set       : the same: sha256 5555"));
            Assert.That(output, Does.Contain("options   : the same but the sampling rate"));
            Assert.That(output, Does.Contain("module    : the same: sha256 1111"));
            Assert.That(output, Does.Contain("wall time, end to end").And.Contain("00:00:23.0").And.Contain("00:00:40.0"));
            Assert.That(output, Does.Contain("start-up 00:00:20.0").And.Contain("connect 00:00:01.5").And.Contain("of the job: queue 00:00:02.0"));
            Assert.That(output, Does.Contain("local     : 1 machine: osx-arm64, 14 cores, 48.0 GiB; 9 session(s) at once (memory)"));
            Assert.That(output, Does.Contain("fleet     : the fleet's machines (not named)"));
            Assert.That(output, Does.Contain("problems a second, end to end").And.Contain("0.13").And.Contain("0.075"));
            Assert.That(output, Does.Contain("rows      : 4 compared row by row; digests: 3 of 3 equal; without a digest: 1 the same"));
            Assert.That(output, Does.Contain("flips (sat, unsat and unknown against each other): 0"));
            Assert.That(output, Does.Contain("measured  : at 3 problem(s) this machine finished first"));
            Assert.That(output, Does.Contain("crossover : the fleet finishes first above 22 problems of this kind, this machine below"));
            Assert.That(output, Does.Contain("local 20 s fixed + 1 s a problem; fleet 39.7 s fixed + 0.1 s a problem"));
            Assert.That(output, Does.Contain("the small set's (1 problem(s), 39.8 s) and this one's (3, 40 s)"));
            Assert.That(output, Does.Contain("result    : the same set, module and options"));
            Assert.That(output, Does.Not.Contain(SmtLiveRunSummary.LOUD));
        });
    }

    [Test]
    public void WithoutASmallRunTheCrossoverIsNotComputedAndSaysWhyTest()
    {
        var exit = Run(null);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK), "the rows are the same; the crossover is information");
        Assert.That(m_output.ToString(), Does.Contain("crossover : not computed: no fixed cost of a fleet run was measured"));
        Assert.That(m_output.ToString(), Does.Contain("lines   : local 20 s fixed + 1 s a problem").And.Not.Contain("assumes"));
    }

    [Test]
    public void ARowWhoseDigestDiffersIsShownAndCountsAsAFlipWhenItsVerdictDiffersTest()
    {
        SmtLiveFixtures.EditRow(Path.Combine(m_fleet, SmtLiveDocuments.RESULTS.File), 0, row =>
        {
            row["verdict"] = "unsat";
            row["digest"] = new string('d', 64);
        });
        SmtLiveFixtures.EditRow(Path.Combine(m_fleet, SmtLiveDocuments.RESULTS.File), 1, row => row["digest"] = new string('e', 64));

        var exit = Run(m_small);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.DIFFER));
            Assert.That(output, Does.Contain("digests: 1 of 3 equal, !!! 2 DIFFER"));
            Assert.That(output, Does.Contain("flips (sat, unsat and unknown against each other): 1"));
            Assert.That(output, Does.Contain("[0] a: digests differ: local sat aaaaaaaaaaaaaaaa fuel 3400, fleet unsat dddddddddddddddd fuel 3400"));
            Assert.That(output, Does.Contain("[1] b: digests differ"));
            Assert.That(output, Does.Contain("result    : THE TWO RUNS DIFFER, row by row"));
        });
    }

    [Test]
    public void RunsOfAnotherSetOrModuleOrOptionsAreNotTheSameThingTest()
    {
        SmtLiveFixtures.SetOfRun(m_fleet, new string('6', 64), 512, 4);
        SmtLiveFixtures.Edit(Path.Combine(m_fleet, SmtLiveDocuments.PROVENANCE.File), root =>
        {
            root["profile"]!["module_sha256"] = new string('7', 64);
            root["options"]!["hard_cap_s"] = 100;
            root["options"]!["sampling_rate"] = 0.05;
        });

        var exit = Run(m_small);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.DIFFER));
            Assert.That(output, Does.Contain("!!! set       : NOT THE SAME"));
            Assert.That(output, Does.Contain("!!! module    : NOT THE SAME"));
            Assert.That(output, Does.Contain("!!! options   : DIFFER: options.hard_cap_s"));
            Assert.That(output, Does.Not.Contain("sampling_rate:"), "the sampling rate is not a difference of what ran");
            Assert.That(output, Does.Contain("THE TWO RUNS DIFFER in what they ran"));
            Assert.That(output, Does.Contain("crossover : not computed: the two runs did not run the same thing").And.Not.Contain("finishes first above"));
        });
    }

    [Test]
    public void AFleetRunThatMeasuredNothingStillComparesItsRowsTest()
    {
        File.Delete(Path.Combine(m_fleet, SmtLiveFleetFacts.FILE));

        var exit = Run(null);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
        Assert.That(m_output.ToString(), Does.Contain("not measured (no fleet.json)"));
        Assert.That(m_output.ToString(), Does.Contain("crossover : not computed: the fleet run has no fleet.json"));
        Assert.That(m_output.ToString(), Does.Contain("digests: 3 of 3 equal"));
    }

    [Test]
    public void AFolderThatIsNotALocalRunIsRefusedTest()
    {
        var exit = new SmtLiveComparative(m_output).Run(m_fleet, m_local, null);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("local.json is not there: the first folder is one local wrote"));
    }

    [Test]
    public void AMissingDocumentIsRefusedTest()
    {
        File.Delete(Path.Combine(m_fleet, SmtLiveDocuments.INTEGRITY.File));

        var exit = Run(null);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("integrity.json is not there"));
    }

    #endregion

    #region Tools

    private int Run(string? small)
    {
        return new SmtLiveComparative(m_output).Run(m_local, m_fleet, small);
    }

    private static string Measured(string file)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "measured", file);
    }

    #endregion
}
