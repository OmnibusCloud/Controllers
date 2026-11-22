using System.Text.RegularExpressions;
using System.Xml.Linq;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Docs;

/// <summary>
/// The scripts package's compatibility marker: the range of controller versions the bundled
/// scripts work with, declared as a package reference so that a restore refuses a pair that
/// cannot work. The range used to end at 1.0.0, exclusive: it would have refused the release the
/// scripts wait for. The marker cannot be on while no controller of the range is on the feed the
/// repository restores from, so until then it sits in the project file commented out, and this
/// test reads it there: the range must admit this build and 1.0.0, must start at the first
/// controller that feed has and no earlier than the scripts' activities, and a build after 1.0.0
/// must have it on. A build of 1.0.0 itself may have it either way: the controller is version
/// 1.0.0 before it is on nuget.org, and the marker is switched on once it is.
/// </summary>
[TestFixture]
public sealed class CorpusScriptsCompatibilityTests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Corpus";

    /// <summary>
    /// The first controller with everything the bundled script calls: <c>Corpus.Disputes</c> with
    /// six arguments (the second dispute round) and <c>Corpus.Collect</c> and
    /// <c>Corpus.Integrity</c> with nine (<c>Corpus.RepairRate</c> is from 0.7.0,
    /// <c>Corpus.ProcessPackage</c> and the packages from 0.5.0).
    /// </summary>
    private const string FIRST_WITH_THE_SCRIPTS_ACTIVITIES = "0.9.0";

    /// <summary>The release the candidates become, and the first controller version on nuget.org.</summary>
    private const string RELEASE = "1.0.0";

    #endregion

    #region Tests

    [Test]
    public void TheRangeAdmitsThisBuildAndTheReleaseItWaitsForTest()
    {
        var (low, high, _) = Marker();
        var build = Version.Parse(CorpusTestRun.VERSION);

        Assert.Multiple(() =>
        {
            Assert.That(low, Is.EqualTo(Version.Parse(RELEASE)), "the range starts at the first controller the feed has: a lower bound no feed has is noise at every restore");
            Assert.That(low, Is.GreaterThanOrEqualTo(Version.Parse(FIRST_WITH_THE_SCRIPTS_ACTIVITIES)), "and no earlier than the first controller that has the scripts' activities");
            Assert.That(Script, Does.Contain("Corpus.ProcessPackage("));
            Assert.That(Script, Does.Contain("Corpus.RepairRate(opts)"), "an activity of 0.7.0");
            Assert.That(Script, Does.Contain("Corpus.Disputes(tasks, executions, repairs, repaired, disputes, resolved)"), "the second dispute round, a call of 0.9.0");
            Assert.That(Script, Does.Contain("Corpus.Integrity(tasks, executions, repairs, repaired, disputes, resolved, undecided, settled, opts)"), "and that round and the options handed to the integrity document");
            Assert.That(build, Is.GreaterThanOrEqualTo(low).And.LessThan(high), "the controller of this build is inside the range");
            Assert.That(Version.Parse(RELEASE), Is.GreaterThanOrEqualTo(low).And.LessThan(high), "and so is the release: a range that ends at 1.0.0 refuses it");
            Assert.That(high, Is.EqualTo(new Version(2, 0, 0)), "the next major after the 1.x line, exclusive");
        });
    }

    [Test]
    public void AReleaseBuildHasTheMarkerOnTest()
    {
        var (_, _, enabled) = Marker();
        var build = Version.Parse(CorpusTestRun.VERSION);
        var release = Version.Parse(RELEASE);

        if (build < release)
        {
            Assert.That(enabled, Is.False, "no controller of the range is on nuget.org before the release: the marker on would break the restore of the scripts project (if one is there now, enable the marker and move this expectation)");
            return;
        }

        if (build == release)
        {
            // The release is made in steps: the version moves to 1.0.0, the controller package
            // goes to nuget.org, the marker is switched on, the scripts package is packed. Before
            // the second step the marker on breaks the restore; after the third it is on.
            TestContext.Out.WriteLine($"The controller is {build}, the release itself: the marker is {(enabled ? "on" : "off until the controller package is on nuget.org")}.");
            return;
        }

        Assert.That(enabled, Is.True, $"the controller is {build}, after the first release: the scripts package must carry its compatibility range. Remove the comment marks around the marker's ItemGroup in {CONTROLLER}.Scripts.csproj (the controller package must be on nuget.org first).");
    }

    #endregion

    #region Tools

    /// <summary>The marker as the project file has it: the range's two ends, and whether it is a live reference or still a comment.</summary>
    private static (Version Low, Version High, bool Enabled) Marker()
    {
        var project = CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Scripts", $"{CONTROLLER}.Scripts.csproj");

        var written = Regex.Matches(project, $"<PackageReference\\s+Include=\"{Regex.Escape(CONTROLLER)}\"\\s+Version=\"([^\"]*)\"\\s*/>");
        Assert.That(written, Has.Count.EqualTo(1), "the project file states the marker once, live or commented");

        var range = Regex.Match(written[0].Groups[1].Value, "^\\[([0-9]+\\.[0-9]+\\.[0-9]+), ([0-9]+\\.[0-9]+\\.[0-9]+)\\)$");
        Assert.That(range.Success, Is.True, $"'{written[0].Groups[1].Value}' is a range with an inclusive start and an exclusive end");

        var enabled = XDocument.Parse(project).Descendants("PackageReference").Any(reference => (string?)reference.Attribute("Include") == CONTROLLER);

        return (Version.Parse(range.Groups[1].Value), Version.Parse(range.Groups[2].Value), enabled);
    }

    #endregion

    #region Properties

    private static string Script => CorpusRepositoryFiles.ReadText("AI", CONTROLLER, "Scripts", "CorpusProcess.wit");

    #endregion
}
