using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Tests.Mock;

namespace OutWit.Controller.Sweep.Tests.Families;

[TestFixture]
public class SweepFamilyOpenFOAMTests
{
    private string m_storage = null!;
    private SweepTestBlobService m_blobs = null!;
    private readonly SweepFamilyOpenFOAM m_family = new();

    [SetUp]
    public void Setup()
    {
        m_storage = Path.Combine(Path.GetTempPath(), $"sweep-foam-family-{Guid.NewGuid():N}");
        m_blobs = new SweepTestBlobService(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storage))
            Directory.Delete(m_storage, recursive: true);
    }

    #region Tools

    private FoamFileRefData File(string relativePath, string text, bool templated = false)
    {
        return new FoamFileRefData { RelativePath = relativePath, BlobId = m_blobs.AddText(text), Templated = templated };
    }

    private SweepOptionsData Study()
    {
        return new SweepOptionsData
        {
            Parameters = [new SweepParameterData { Name = "U", Token = "{{oc1}}" }, new SweepParameterData { Name = "nu", Token = "{{oc2}}" }],
            Variants =
            [
                new SweepVariantData { VariantIndex = 0, Values = ["10", "1e-05"] },
                new SweepVariantData { VariantIndex = 1, Values = ["20", "2e-05"] }
            ],
            OpenFOAM = new FoamCaseData
            {
                BaseFiles =
                [
                    File("system/controlDict", "application simpleFoam;\n"),
                    File("0/U", "internalField uniform ({{oc1}} 0 0);\n", templated: true),
                    File("constant/transportProperties", "nu {{oc2}};\n", templated: true)
                ],
                Recipe = new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "simpleFoam" }] }
            }
        };
    }

    #endregion

    #region Validation Tests

    [Test]
    public async Task ACaseWhoseTokensArePlacedIsAcceptedTest()
    {
        Assert.That(await m_family.ValidateAsync(Study(), m_blobs), Is.Empty);
    }

    [Test]
    public async Task TheCaseRulesOfTheOpenFOAMModelApplyAtThePlanTest()
    {
        var options = Study();
        options.OpenFOAM?.BaseFiles.Add(File("processor0/0/U", "x"));

        var findings = await m_family.ValidateAsync(options, m_blobs);

        Assert.That(findings, Has.Exactly(1).Items.And.Some.StartsWith("processor0/0/U:"));
    }

    [Test]
    public async Task TokenCoverageIsCheckedOverTheTemplatedFilesOnlyTest()
    {
        var options = Study();
        options.Parameters[1].Token = "{{oc3}}";
        options.OpenFOAM?.BaseFiles.Add(File("constant/polyMesh/points", "{{oc9}} is not a token here: the file is not templated"));

        var findings = await m_family.ValidateAsync(options, m_blobs);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "Token {{oc3}} occurs in no templated file of the case.",
            "constant/transportProperties: token {{oc2}} is not declared by the study."
        }));
    }

    [Test]
    public async Task AStudyWithoutItsBlockIsRefusedTest()
    {
        Assert.That(await m_family.ValidateAsync(new SweepOptionsData(), m_blobs), Is.EqualTo(new[] { "The study carries no OpenFOAM block." }));
    }

    #endregion

    #region Task Tests

    [Test]
    public async Task EachTaskIsTheCaseAndTheVariantsValuesTest()
    {
        var options = Study();
        var uploadsBefore = Directory.GetFiles(m_storage).Length;

        var tasks = await m_family.MakeTasksAsync(options, options.Variants, m_blobs);

        Assert.That(tasks, Has.Count.EqualTo(2));
        var second = tasks[1] as FoamTaskData ?? new FoamTaskData();
        Assert.That(second.VariantIndex, Is.EqualTo(1));
        Assert.That(second.Case, Is.SameAs(options.OpenFOAM), "the case travels as the study carries it");
        Assert.That(second.Substitutions.Select(value => (value.Token, value.Value)), Is.EqualTo(new[] { ("{{oc1}}", "20"), ("{{oc2}}", "2e-05") }));
        Assert.That(Directory.GetFiles(m_storage).Length, Is.EqualTo(uploadsBefore), "a case chunk is metadata only: nothing is uploaded");
    }

    #endregion

    #region Row Tests

    [TestCase(0, null, 0, SweepOutcome.Succeeded)]
    [TestCase(1, "blockMesh", 0, SweepOutcome.Failed)]
    [TestCase(0, null, 1, SweepOutcome.Refused)]
    public void TheVerdictFollowsTheResultTest(int exitCode, string? failedStep, int rejections, SweepOutcome expected)
    {
        var result = new FoamResultData
        {
            ExitCode = exitCode,
            FailedStep = failedStep,
            Rejections = Enumerable.Repeat("0/p:1: codeStream compiles C++ at run time.", rejections).ToList()
        };

        Assert.That(SweepFamilyOpenFOAM.OutcomeOf(result), Is.EqualTo(expected));
    }

    [Test]
    public void AnUnconvergedRunThatFinishedCleanlyIsSucceededTest()
    {
        Assert.That(SweepFamilyOpenFOAM.OutcomeOf(new FoamResultData { ExitCode = 0, Converged = false }), Is.EqualTo(SweepOutcome.Succeeded), "convergence is a fact, not a verdict");
    }

    [Test]
    public void ARowCarriesTheResultAndItsCaseZipTest()
    {
        var result = new FoamResultData { VariantIndex = 3, ArtifactBlobId = Guid.NewGuid(), ArtifactBytes = 4096 };

        var row = m_family.ToRows(new object[] { result }).Single();

        Assert.That(row.OpenFOAM, Is.SameAs(result));
        Assert.That(row.CalculiX, Is.Null);
        var artifact = m_family.ArtifactsOf(row).Single();
        Assert.That((artifact.Kind, artifact.BlobId, artifact.Bytes), Is.EqualTo((SweepArtifactKind.OpenFOAMCase, result.ArtifactBlobId ?? Guid.Empty, 4096L)));
        Assert.That(m_family.ArtifactsOf(new SweepManifestRowData { OpenFOAM = new FoamResultData() }), Is.Empty);
    }

    #endregion
}
