using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamCheckMeshReaderTests
{
    private const string MESH_OK =
        "Mesh stats\n    points:           25012\n    faces:            49180\n    internal faces:   24170\n    cells:            12225\n" +
        "    faces per cell:   6\n\nChecking topology...\n    Boundary definition OK.\n\nMesh OK.\n\nEnd\n";

    private const string MESH_FAILED =
        "Mesh stats\n    cells:            48\n\nChecking geometry...\n ***Boundary openness (1 0 0) possible hole in boundary description.\n" +
        " ***High aspect ratio cells found, Max aspect ratio: 1e+15, number of cells 48\n\nFailed 2 mesh checks.\n\nEnd\n";

    private string m_dir = null!;

    [SetUp]
    public void Setup()
    {
        m_dir = OpenFOAMTestPaths.CreateScratch("foam-checkmesh");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_dir);
    }

    #region Reading Tests

    [Test]
    public void AGoodMeshIsOkWithItsCellCountTest()
    {
        var log = Path.Combine(m_dir, "log.checkMesh");
        File.WriteAllText(log, MESH_OK);

        var (verdict, cells) = FoamCheckMeshReader.Read(log);

        Assert.That(verdict, Is.EqualTo(FoamCheckMeshReader.VERDICT_OK));
        Assert.That(cells, Is.EqualTo(12225));
    }

    [Test]
    public void AFailedMeshNamesTheNumberOfFailedChecksTest()
    {
        var log = Path.Combine(m_dir, "log.checkMesh");
        File.WriteAllText(log, MESH_FAILED);

        var (verdict, cells) = FoamCheckMeshReader.Read(log);

        Assert.That(verdict, Is.EqualTo("failed 2 checks"));
        Assert.That(cells, Is.EqualTo(48));
    }

    [Test]
    public void AMissingOrTruncatedLogHasNoVerdictTest()
    {
        var (verdict, cells) = FoamCheckMeshReader.Read(Path.Combine(m_dir, "log.checkMesh"));
        Assert.That(verdict, Is.Null);
        Assert.That(cells, Is.EqualTo(0));

        var truncated = Path.Combine(m_dir, "log.truncated");
        File.WriteAllText(truncated, "Mesh stats\n    cells:            100\nChecking topology...\n");
        var (verdict2, cells2) = FoamCheckMeshReader.Read(truncated);
        Assert.That(verdict2, Is.Null, "a log that says neither OK nor Failed has no verdict");
        Assert.That(cells2, Is.EqualTo(100));
    }

    #endregion
}
