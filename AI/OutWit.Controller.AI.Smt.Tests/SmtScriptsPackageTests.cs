using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// The scripts package (<c>OutWit.Controller.AI.Smt.Scripts</c>): it packs the bundled scripts the
/// controller's folder holds, and depends on the controller by its compatibility marker, a
/// half-open range from 1.0.0, the controller's first release and the first version on nuget.org
/// (the first with every activity the scripts call: Smt.Forget is new in it), to the next major.
/// The scripts' 1.0.0 went out without the marker, so the package that carries it is a later
/// version.
/// </summary>
[TestFixture]
public sealed class SmtScriptsPackageTests
{
    #region Constants

    private const string SCRIPTS_PROJECT = "AI/OutWit.Controller.AI.Smt.Scripts/OutWit.Controller.AI.Smt.Scripts.csproj";
    private const string SCRIPTS_FOLDER = "AI/OutWit.Controller.AI.Smt/Scripts";
    private const string CONTROLLER_PACKAGE = "OutWit.Controller.AI.Smt";

    private static readonly Version FIRST_RELEASE = new(1, 0, 0);
    private static readonly string[] SCRIPTS = ["SmtPreflight.wit", "SmtRun.wit", "SmtRuntimeDiagnostics.wit"];

    #endregion

    #region Tests

    [Test]
    public void TheMarkerStartsAtTheFirstReleaseTest()
    {
        var text = Read(SCRIPTS_PROJECT);
        var marker = Regex.Match(text, $"<PackageReference\\s+Include=\"{Regex.Escape(CONTROLLER_PACKAGE)}\"\\s+Version=\"\\[(?<lower>[0-9.]+),\\s*(?<upper>[0-9.]+)\\)\"");

        Assert.That(marker.Success, Is.True, "the scripts project carries its compatibility marker as a half-open range");

        var lower = Version.Parse(marker.Groups["lower"].Value);
        var upper = Version.Parse(marker.Groups["upper"].Value);

        Assert.Multiple(() =>
        {
            Assert.That(lower, Is.EqualTo(FIRST_RELEASE), "the range starts at the controller's first release: the first version on nuget.org, and the first with every activity the scripts call");
            Assert.That(upper, Is.EqualTo(new Version(lower.Major + 1, 0, 0)), "it ends at the next major, exclusive, as the decision record says");
        });
    }

    [Test]
    public void TheMarkerIsEnabledTest()
    {
        var text = Read(SCRIPTS_PROJECT);
        var marker = text.IndexOf($"<PackageReference Include=\"{CONTROLLER_PACKAGE}\"", StringComparison.Ordinal);
        var opened = text.LastIndexOf("<!--", marker, StringComparison.Ordinal);
        var closed = text.LastIndexOf("-->", marker, StringComparison.Ordinal);
        var version = Version.Parse(Regex.Match(text, "<Version>(?<v>[^<]+)</Version>").Groups["v"].Value);

        Assert.Multiple(() =>
        {
            Assert.That(marker, Is.GreaterThan(0));
            Assert.That(opened, Is.LessThan(closed), "the controller is on nuget.org: the reference is part of the project, not a comment");
            Assert.That(version, Is.GreaterThan(FIRST_RELEASE), "the scripts' 1.0.0 went out without the marker: the package with it is a later version");
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
