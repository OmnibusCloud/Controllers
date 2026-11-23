using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// The scripts package (<c>OutWit.Controller.AI.Smt.Scripts</c>): it packs the bundled scripts the
/// controller's folder holds, and its compatibility marker, written and commented out while no
/// controller is on nuget.org, holds the release it waits for. The first public release of a
/// product is 1.0.0 at the earliest, so a range that ends at 1.0.0 (the next major of 0.3.0, the
/// first version whose activities the scripts call) would refuse the very release it waits for.
/// </summary>
[TestFixture]
public sealed class SmtScriptsPackageTests
{
    #region Constants

    private const string SCRIPTS_PROJECT = "AI/OutWit.Controller.AI.Smt.Scripts/OutWit.Controller.AI.Smt.Scripts.csproj";
    private const string SCRIPTS_FOLDER = "AI/OutWit.Controller.AI.Smt/Scripts";
    private const string CONTROLLER_PACKAGE = "OutWit.Controller.AI.Smt";

    private static readonly Version FIRST_PUBLIC = new(1, 0, 0);
    private static readonly Version ACTIVITIES_SINCE = new(0, 3, 0);
    private static readonly string[] SCRIPTS = ["SmtPreflight.wit", "SmtRun.wit", "SmtRuntimeDiagnostics.wit"];

    #endregion

    #region Tests

    [Test]
    public void TheMarkerAcceptsTheReleaseItWaitsForTest()
    {
        var text = Read(SCRIPTS_PROJECT);
        var marker = Regex.Match(text, $"<PackageReference\\s+Include=\"{Regex.Escape(CONTROLLER_PACKAGE)}\"\\s+Version=\"\\[(?<lower>[0-9.]+),\\s*(?<upper>[0-9.]+)\\)\"");

        Assert.That(marker.Success, Is.True, "the scripts project carries its compatibility marker as a half-open range");

        var lower = Version.Parse(marker.Groups["lower"].Value);
        var upper = Version.Parse(marker.Groups["upper"].Value);

        Assert.Multiple(() =>
        {
            Assert.That(lower, Is.GreaterThanOrEqualTo(ACTIVITIES_SINCE), "no lower than the first version with the activities the scripts call");
            Assert.That(lower, Is.LessThanOrEqualTo(FIRST_PUBLIC), "the range starts at or before the controller's first public release");
            Assert.That(upper, Is.GreaterThan(FIRST_PUBLIC), "and does not end before it");
            Assert.That(upper, Is.EqualTo(new Version(lower.Major + 1, 0, 0)), "it ends at the next major, exclusive, as the decision record says");
        });
    }

    [Test]
    public void TheMarkerWaitsCommentedOutTest()
    {
        var text = Read(SCRIPTS_PROJECT);
        var marker = text.IndexOf($"<PackageReference Include=\"{CONTROLLER_PACKAGE}\"", StringComparison.Ordinal);
        var opened = text.LastIndexOf("<!--", marker, StringComparison.Ordinal);
        var closed = text.LastIndexOf("-->", marker, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(marker, Is.GreaterThan(0));
            Assert.That(opened, Is.GreaterThan(closed), "no feed has the controller yet: a reference to it would break every restore");
        });
    }

    [Test]
    public void ThePackageCarriesTheBundledScriptsTest()
    {
        var text = Read(SCRIPTS_PROJECT);
        var root = SmtRigModules.SolutionRoot()!;
        var scripts = Directory.GetFiles(Path.Combine(root, SCRIPTS_FOLDER), "*.wit").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(scripts, Is.EqualTo(SCRIPTS));
            Assert.That(text, Does.Contain("<None Include=\"..\\OutWit.Controller.AI.Smt\\Scripts\\*.wit\""));
            Assert.That(text, Does.Contain("PackagePath=\"content/scripts/\""));
            Assert.That(File.Exists(Path.Combine(root, "AI", "OutWit.Controller.AI.Smt.Scripts", "build", "OutWit.Controller.AI.Smt.Scripts.targets")), Is.True, "the consumer's targets that stage the scripts");
        });
    }

    #endregion

    #region Tools

    private static string Read(string relative)
    {
        return File.ReadAllText(Path.Combine(SmtRigModules.SolutionRoot()!, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    #endregion
}
