using System.Text;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
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
        return m_blobs.AddFile(relativePath, text, templated);
    }

    #endregion

    #region Materialisation Tests

    [Test]
    public async Task FilesLandAtTheirPathsAndTemplatedOnesAreInstantiatedTest()
    {
        var binary = new string('\u00FF', 8) + "\0\u0001binary field\r\n";
        var task = new FoamTaskData
        {
            Case = new FoamCaseData
            {
                BaseFiles =
                [
                    BlobFile("system/controlDict", "application simpleFoam;\r\nendTime {{oc2}};\r\n", templated: true),
                    BlobFile("0/U", "internalField uniform ({{oc1}} 0 0);\n", templated: true),
                    BlobFile("constant/polyMesh/points", binary),
                    BlobFile("constant/transportProperties", "nu {{oc1}};\n")
                ]
            },
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
    public async Task ATemplatedFileKeepsEveryByteOutsideItsTokensTest()
    {
        // A dictionary saved by a Windows editor: a BOM, a Latin-1 comment, a
        // byte pair that is not UTF-8, CRLF endings. Only the token changes.
        var head = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'/', (byte)'/', (byte)' ', 0xE9, 0xC3, 0x28, 0x0D, 0x0A };
        var content = head.Concat(Encoding.ASCII.GetBytes("nu {{oc1}};\r\n")).ToArray();
        var task = new FoamTaskData
        {
            Case = new FoamCaseData
            {
                BaseFiles = [m_blobs.AddFile("constant/transportProperties", content, templated: true)]
            },
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = "2e-05" }]
        };

        var findings = await FoamCaseMaterializer.MaterializeAsync(task, m_case, m_blobs);

        Assert.That(findings, Is.Empty);
        Assert.That(File.ReadAllBytes(Path.Combine(m_case, "constant", "transportProperties")),
            Is.EqualTo(head.Concat(Encoding.ASCII.GetBytes("nu 2e-05;\r\n")).ToArray()));
    }

    [Test]
    public async Task ALeftoverTokenAndABadPathAreFindingsAndTheRestStillLandsTest()
    {
        var task = new FoamTaskData
        {
            Case = new FoamCaseData
            {
                BaseFiles =
                [
                    BlobFile("0/U", "internalField uniform ({{oc1}} {{oc3}} 0);\n", templated: true),
                    BlobFile("../escape", "x\n"),
                    BlobFile("system/controlDict", "application simpleFoam;\n")
                ]
            },
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
        var task = new FoamTaskData { Case = new FoamCaseData { BaseFiles = [file] } };

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
    public async Task ABlobWhoseBytesAreNotTheDescribedOnesRefusesTheVariantAndTheRestStillLandsTest()
    {
        // The case changed between the reading that made the study and the
        // upload: the reference describes the old bytes, the blob holds the new.
        var changed = BlobFile("system/controlDict", "application simpleFoam;\nendTime 200;\n");
        File.WriteAllText(m_blobs.GetStoredPath(changed.BlobId), "application simpleFoam;\nendTime 300;\n");
        var (size, sha256) = await FoamCaseBlobRules.DescribeAsync(m_blobs.GetStoredPath(changed.BlobId));
        var task = new FoamTaskData { Case = new FoamCaseData { BaseFiles = [changed, BlobFile("0/U", "internalField uniform (1 0 0);\n")] } };

        var findings = await FoamCaseMaterializer.MaterializeAsync(task, m_case, m_blobs);

        Assert.That(findings, Is.EqualTo(new[]
        {
            $"system/controlDict: the blob's bytes are not the ones the task describes (size {size}, sha256 {sha256}; expected size {changed.Size}, sha256 {changed.Sha256}) - the case changed after it was read; open it again and submit it again."
        }));
        Assert.That(File.Exists(Path.Combine(m_case, "system", "controlDict")), Is.False, "the other bytes never land in the case");
        Assert.That(File.Exists(Path.Combine(m_case, "0", "U")), Is.True, "the rest of the case still lands");
    }

    [Test]
    public async Task ATaskWithoutACaseOrWithoutFilesIsAFindingTest()
    {
        var noCase = await FoamCaseMaterializer.MaterializeAsync(new FoamTaskData(), m_case, m_blobs);
        var noFiles = await FoamCaseMaterializer.MaterializeAsync(new FoamTaskData { Case = new FoamCaseData() }, m_case, m_blobs);

        Assert.That(noCase, Is.EqualTo(new[] { "The task carries no case." }));
        Assert.That(noFiles, Is.EqualTo(new[] { "The task carries no case files." }));
    }

    #endregion
}
