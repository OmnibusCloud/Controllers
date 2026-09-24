using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamCaseMaterializerTests
{
    private string m_root = null!;
    private string m_case = null!;
    private FoamTestBlobService m_blobs = null!;

    [SetUp]
    public void Setup()
    {
        m_root = OpenFOAMTestPaths.CreateScratch("foam-materialize");
        m_case = Path.Combine(m_root, "case");
        Directory.CreateDirectory(m_case);
        m_blobs = new FoamTestBlobService(Path.Combine(m_root, "blobs"));
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_root);
    }

    #region Tools

    private FoamFileRefData BlobFile(string relativePath, string text, bool templated = false)
    {
        return new FoamFileRefData
        {
            RelativePath = relativePath,
            BlobId = m_blobs.AddText(text),
            Sha256 = "n/a",
            Size = text.Length,
            Templated = templated
        };
    }

    #endregion

    #region Path Tests

    [TestCase("system/controlDict", null)]
    [TestCase("0/U", null)]
    [TestCase("constant/polyMesh/points", null)]
    [TestCase("", "no path")]
    [TestCase("system\\controlDict", "forward slashes")]
    [TestCase("system/control Dict", "space")]
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
        var finding = FoamCaseMaterializer.ValidatePath(relativePath);

        if (expectedFragment == null)
            Assert.That(finding, Is.Null);
        else
            Assert.That(finding, Does.Contain(expectedFragment));
    }

    #endregion

    #region Materialisation Tests

    [Test]
    public async Task FilesLandAtTheirPathsAndTemplatedOnesAreInstantiatedTest()
    {
        var binary = new string('\u00FF', 8) + "\0\u0001binary field\r\n";
        var task = new FoamTaskData
        {
            BaseFiles =
            [
                BlobFile("system/controlDict", "application simpleFoam;\r\nendTime {{oc2}};\r\n", templated: true),
                BlobFile("0/U", "internalField uniform ({{oc1}} 0 0);\n", templated: true),
                BlobFile("constant/polyMesh/points", binary),
                BlobFile("constant/transportProperties", "nu {{oc1}};\n")
            ],
            Substitutions =
            [
                new FoamTokenValueData { Token = "{{oc1}}", Value = "12.5" },
                new FoamTokenValueData { Token = "{{oc2}}", Value = "300" }
            ]
        };

        var findings = await FoamCaseMaterializer.MaterializeAsync(task, m_case, m_blobs);

        Assert.That(findings, Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(m_case, "system", "controlDict")), Is.EqualTo("application simpleFoam;\r\nendTime 300;\r\n"), "line endings survive");
        Assert.That(File.ReadAllText(Path.Combine(m_case, "0", "U")), Is.EqualTo("internalField uniform (12.5 0 0);\n"));
        Assert.That(File.ReadAllText(Path.Combine(m_case, "constant", "polyMesh", "points")), Is.EqualTo(binary), "a file not marked templated is copied byte for byte");
        Assert.That(File.ReadAllText(Path.Combine(m_case, "constant", "transportProperties")), Is.EqualTo("nu {{oc1}};\n"), "tokens in a file not marked templated are left alone");
    }

    [Test]
    public async Task ALeftoverTokenAndABadPathAreFindingsAndTheRestStillLandsTest()
    {
        var task = new FoamTaskData
        {
            BaseFiles =
            [
                BlobFile("0/U", "internalField uniform ({{oc1}} {{oc3}} 0);\n", templated: true),
                BlobFile("../escape", "x\n"),
                BlobFile("system/controlDict", "application simpleFoam;\n")
            ],
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = "1" }]
        };

        var findings = await FoamCaseMaterializer.MaterializeAsync(task, m_case, m_blobs);

        Assert.That(findings, Has.Count.EqualTo(2));
        Assert.That(findings, Has.Some.EqualTo("0/U: token {{oc3}} has no value in this variant."));
        Assert.That(findings, Has.Some.Contains("../escape"));
        Assert.That(File.Exists(Path.Combine(m_case, "system", "controlDict")), Is.True);
        Assert.That(File.Exists(Path.Combine(m_root, "escape")), Is.False, "nothing is written outside the case");
    }

    [Test]
    public async Task AReadOnlyBlobLandsWritableTest()
    {
        // A blob cache may keep its files read-only; the case is the run's to
        // write into and the scratch cleanup's to delete.
        var file = BlobFile("constant/polyMesh/points", "points\n");
        File.SetAttributes(m_blobs.GetStoredPath(file.BlobId), FileAttributes.ReadOnly);
        var task = new FoamTaskData { BaseFiles = [file] };

        try
        {
            var findings = await FoamCaseMaterializer.MaterializeAsync(task, m_case, m_blobs);

            Assert.That(findings, Is.Empty);
            var target = Path.Combine(m_case, "constant", "polyMesh", "points");
            Assert.That(File.GetAttributes(target) & FileAttributes.ReadOnly, Is.EqualTo((FileAttributes)0));
            File.WriteAllText(target, "rewritten\n");
        }
        finally
        {
            File.SetAttributes(m_blobs.GetStoredPath(file.BlobId), FileAttributes.Normal);
        }
    }

    [Test]
    public async Task ATaskWithoutFilesIsAFindingTest()
    {
        var findings = await FoamCaseMaterializer.MaterializeAsync(new FoamTaskData(), m_case, m_blobs);

        Assert.That(findings, Is.EqualTo(new[] { "The task carries no case files." }));
    }

    #endregion
}
