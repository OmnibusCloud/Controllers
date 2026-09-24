using System.IO.Compression;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamArtifactPackerTests
{
    private string m_scratch = null!;
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_scratch = OpenFOAMTestPaths.CreateScratch("foam-pack");
        m_case = Path.Combine(m_scratch, "case");
        foreach (var relative in new[] { "system/controlDict", "constant/transportProperties", "constant/polyMesh/points", "0/U", "100/U", "250/U", "250/p", "processor0/250/U", "postProcessing/coeffs/250/coefficient.dat", "log.blockMesh", "log.simpleFoam" })
        {
            var path = Path.Combine(m_case, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relative);
        }
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_scratch);
    }

    #region Tools

    private IReadOnlyList<string> Entries(FoamArtifactPolicyData policy)
    {
        var zip = Path.Combine(m_scratch, "artifact.zip");
        var bytes = FoamArtifactPacker.Pack(m_case, policy, zip);
        Assert.That(bytes, Is.GreaterThan(0));

        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    #endregion

    #region Packing Tests

    [Test]
    public void TheLatestTimeWithMeshAndLogsIsTheParaViewSetTest()
    {
        var entries = Entries(new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Mesh = true, Logs = true });

        Assert.That(entries, Does.Contain("system/controlDict"));
        Assert.That(entries, Does.Contain("constant/transportProperties"));
        Assert.That(entries, Does.Contain("constant/polyMesh/points"));
        Assert.That(entries, Does.Contain("250/U").And.Contain("250/p"));
        Assert.That(entries, Does.Not.Contain("100/U").And.Not.Contain("0/U"));
        Assert.That(entries, Does.Contain("log.simpleFoam"));
        Assert.That(entries, Does.Not.Contain("processor0/250/U"));
        Assert.That(entries, Does.Not.Contain("postProcessing/coeffs/250/coefficient.dat"));
        Assert.That(entries, Does.Contain(FoamArtifactPacker.STUB));
    }

    [Test]
    public void AllTimesWithoutMeshKeepsEveryTimeDirectoryAndDropsThePolyMeshTest()
    {
        var entries = Entries(new FoamArtifactPolicyData { Times = FoamArtifactTimes.All, PostProcessing = true });

        Assert.That(entries, Does.Contain("0/U").And.Contain("100/U").And.Contain("250/U"));
        Assert.That(entries, Does.Not.Contain("constant/polyMesh/points"));
        Assert.That(entries, Does.Contain("postProcessing/coeffs/250/coefficient.dat"));
        Assert.That(entries, Does.Not.Contain("log.blockMesh"));
    }

    [Test]
    public void TimeDirectoriesAreOrderedNumericallyTest()
    {
        var all = FoamArtifactPacker.TimeDirectories(m_case, FoamArtifactTimes.All).Select(Path.GetFileName).ToList();
        var latest = FoamArtifactPacker.TimeDirectories(m_case, FoamArtifactTimes.Latest).Select(Path.GetFileName).ToList();

        Assert.That(all, Is.EqualTo(new[] { "0", "100", "250" }));
        Assert.That(latest, Is.EqualTo(new[] { "250" }));
        Assert.That(FoamArtifactPacker.TimeDirectories(m_case, FoamArtifactTimes.None), Is.Empty);
    }

    #endregion
}
