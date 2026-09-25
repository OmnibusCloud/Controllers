using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamCasePathRulesTests
{
    #region Tools

    private static FoamFileRefData File(string relativePath)
    {
        return new FoamFileRefData { RelativePath = relativePath, BlobId = Guid.NewGuid(), Sha256 = "n/a", Size = 1 };
    }

    #endregion

    #region Path Tests

    [TestCase("system/controlDict", null)]
    [TestCase("0/U", null)]
    [TestCase("constant/polyMesh/points", null)]
    [TestCase("", "no path")]
    [TestCase("system\\controlDict", "forward slashes")]
    [TestCase("system/control Dict", "space")]
    [TestCase("system/control\tDict", "whitespace")]
    [TestCase("system/control Dict", "whitespace")]
    [TestCase("../other/controlDict", "inside the case")]
    [TestCase("/etc/passwd", "inside the case")]
    [TestCase("C:/Windows/x", "inside the case")]
    [TestCase("system//controlDict", "empty or '.' segments")]
    [TestCase("./system/controlDict", "empty or '.' segments")]
    [TestCase("log.simpleFoam", "earlier run")]
    [TestCase("log.blockMesh.2", "earlier run")]
    [TestCase("logs/simpleFoam.txt", null)]
    [TestCase("postProcessing/coeffs/0/coefficient.dat", "earlier run")]
    [TestCase("processor0/0/U", "decomposed case")]
    [TestCase("processor12/constant/polyMesh/points", "decomposed case")]
    [TestCase("processorX/0/U", null)]
    [TestCase("constant/triSurface/motorBike.obj.gz", null)]
    public void PathRulesAreAppliedTest(string relativePath, string? expectedFragment)
    {
        var finding = FoamCasePathRules.Validate(relativePath);

        if (expectedFragment == null)
            Assert.That(finding, Is.Null);
        else
            Assert.That(finding, Does.Contain(expectedFragment));
    }

    [TestCase("system/blockMeshDict", false)]
    [TestCase("../x", true)]
    [TestCase("a/../../x", true)]
    [TestCase("/abs", true)]
    [TestCase("\\abs", true)]
    [TestCase("D:x", true)]
    [TestCase("a..b/c", false)]
    public void AnEscapeIsAbsoluteDriveRootedOrClimbingTest(string value, bool escapes)
    {
        Assert.That(FoamCasePathRules.IsPathEscape(value), Is.EqualTo(escapes));
    }

    [TestCase("system/controlDict", false)]
    [TestCase("/home/node/Application Support", true)]
    [TestCase("C:\\kit\tbin", true)]
    [TestCase("kit\u00A0bin", true)]
    [TestCase("kit\nbin", true)]
    public void WhitespaceOfAnyKindIsFoundTest(string path, bool expected)
    {
        Assert.That(FoamCasePathRules.HasWhitespace(path), Is.EqualTo(expected));
    }

    #endregion

    #region Tree Tests

    [Test]
    public void ACleanTreeHasNoFindingsTest()
    {
        Assert.That(FoamCasePathRules.ValidateTree([File("system/controlDict"), File("0/U"), File("0/p"), File("constant/transportProperties")]), Is.Empty);
    }

    [Test]
    public void AnEmptyTreeIsOneFindingTest()
    {
        Assert.That(FoamCasePathRules.ValidateTree([]), Is.EqualTo(new[] { "The task carries no case files." }));
    }

    [Test]
    public void ARepeatedFileAndACaseOnlyCollisionAreFindingsTest()
    {
        var findings = FoamCasePathRules.ValidateTree([File("0/U"), File("0/U"), File("0/u"), File("system/controlDict")]);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "0/U: the case carries this file twice.",
            "0/u: differs from 0/U by case alone - one file on a Windows or macOS node."
        }));
    }

    [Test]
    public void EveryBadPathIsNamedAndTheGoodOnesPassTest()
    {
        var findings = FoamCasePathRules.ValidateTree([File("system/controlDict"), File("../x"), File("log.simpleFoam")]);

        Assert.That(findings, Has.Count.EqualTo(2));
        Assert.That(findings[0], Does.StartWith("../x:"));
        Assert.That(findings[1], Does.StartWith("log.simpleFoam:"));
    }

    #endregion
}
