using MemoryPack;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Tests.Model;

[TestFixture]
public class FoamDataRoundTripTests
{
    #region Tools

    private static FoamTaskData CreateTask()
    {
        return new FoamTaskData
        {
            VariantIndex = 7,
            BaseFiles =
            [
                new FoamFileRefData { RelativePath = "system/controlDict", BlobId = Guid.NewGuid(), Sha256 = "ab", Size = 120 },
                new FoamFileRefData { RelativePath = "0/U", BlobId = Guid.NewGuid(), Sha256 = "cd", Size = 340, Templated = true }
            ],
            Substitutions = [new FoamTokenValueData { Token = "{{oc1}}", Value = "12.5" }],
            Recipe = new FoamRecipeData
            {
                Application = "simpleFoam",
                Steps =
                [
                    new FoamStepData { Utility = "blockMesh" },
                    new FoamStepData { Utility = "simpleFoam", Parallel = true },
                    new FoamStepData { Utility = "postProcess", Arguments = ["-func", "coeffs", "-latestTime"] }
                ]
            },
            Threads = 4,
            Extraction = new FoamExtractionRequestData
            {
                Responses =
                [
                    new FoamResponseSpecData
                    {
                        Name = "coeffs",
                        Kind = FoamResponseKind.ForceCoeffs,
                        Patches = ["motorBikeGroup"],
                        Parameters = [new FoamNamedValueData { Name = "magUInf", Value = "20" }]
                    }
                ]
            },
            ArtifactPolicy = new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest, Mesh = true },
            CellCount = 350_000,
            SolverClass = "incompressible-steady",
            TimeBudgetSeconds = 600
        };
    }

    private static FoamResultData CreateResult()
    {
        return new FoamResultData
        {
            VariantIndex = 7,
            Steps = [new FoamStepOutcomeData { Utility = "blockMesh", Ranks = 1, Seconds = 0.4 }, new FoamStepOutcomeData { Utility = "simpleFoam", Ranks = 4, Seconds = 88.1 }],
            TotalSeconds = 91.2,
            Converged = true,
            Iterations = 281,
            FinalTime = 281,
            FinalResiduals = [new FoamResponseValueData { Name = "residual.p", Value = 9.1e-5 }],
            CellCount = 12225,
            CheckMeshVerdict = "ok",
            WarningCount = 1,
            ResponseRow = new FoamResponseRowData { Values = [new FoamResponseValueData { Name = "coeffs.Cd", Value = 0.418 }] },
            ArtifactBlobId = Guid.NewGuid(),
            ArtifactBytes = 1_234_567
        };
    }

    #endregion

    #region Round Trip Tests

    [Test]
    public void TaskSurvivesMemoryPackRoundTripTest()
    {
        var task = CreateTask();
        var restored = MemoryPackSerializer.Deserialize<FoamTaskData>(MemoryPackSerializer.Serialize(task));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(task), Is.True);
    }

    [Test]
    public void ResultSurvivesMemoryPackRoundTripTest()
    {
        var result = CreateResult();
        var restored = MemoryPackSerializer.Deserialize<FoamResultData>(MemoryPackSerializer.Serialize(result));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(result), Is.True);
    }

    [Test]
    public void RefusedResultSurvivesMemoryPackRoundTripTest()
    {
        var result = new FoamResultData { VariantIndex = 2, Rejections = ["system/controlDict:12: codeStream compiles C++ at run time."] };
        var restored = MemoryPackSerializer.Deserialize<FoamResultData>(MemoryPackSerializer.Serialize(result));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Is(result), Is.True);
        Assert.That(restored.ToString(), Does.Contain("refused"));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void TaskCloneIsValueEqualAndIndependentTest()
    {
        var task = CreateTask();
        var clone = task.Clone();

        Assert.That(clone.Is(task), Is.True);

        clone.Recipe!.Steps[0].Utility = "snappyHexMesh";
        Assert.That(clone.Is(task), Is.False);
        Assert.That(task.Recipe!.Steps[0].Utility, Is.EqualTo("blockMesh"));

        var other = task.Clone();
        other.Substitutions[0].Value = "13";
        Assert.That(other.Is(task), Is.False);
    }

    [Test]
    public void ResultDifferingByConvergenceIsNotEqualTest()
    {
        var result = CreateResult();
        var other = result.Clone();
        other.Converged = false;

        Assert.That(other.Is(result), Is.False);
    }

    [Test]
    public void EmptyArtifactPolicyIsRecognisedTest()
    {
        Assert.That(new FoamArtifactPolicyData().IsEmpty(), Is.True);
        Assert.That(new FoamArtifactPolicyData { Logs = true }.IsEmpty(), Is.False);
        Assert.That(new FoamArtifactPolicyData { Times = FoamArtifactTimes.Latest }.IsEmpty(), Is.False);
    }

    #endregion
}
