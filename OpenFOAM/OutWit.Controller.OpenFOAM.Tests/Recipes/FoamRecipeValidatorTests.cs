using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Recipes;

namespace OutWit.Controller.OpenFOAM.Tests.Recipes;

[TestFixture]
public class FoamRecipeValidatorTests
{
    #region Tools

    private static FoamRecipeData MotorBike()
    {
        return new FoamRecipeData
        {
            Application = "simpleFoam",
            MeshesPerVariant = true,
            Steps =
            [
                new FoamStepData { Utility = "surfaceFeatureExtract" },
                new FoamStepData { Utility = "blockMesh" },
                new FoamStepData { Utility = "decomposePar" },
                new FoamStepData { Utility = "snappyHexMesh", Arguments = ["-overwrite"], Parallel = true },
                new FoamStepData { Utility = "topoSet", Parallel = true },
                new FoamStepData { Utility = "potentialFoam", Arguments = ["-writephi"], Parallel = true },
                new FoamStepData { Utility = "simpleFoam", Parallel = true },
                new FoamStepData { Utility = "reconstructParMesh", Arguments = ["-constant"] },
                new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] },
                new FoamStepData { Utility = "postProcess", Arguments = ["-func", "forceCoeffs", "-latestTime"] }
            ]
        };
    }

    #endregion

    #region Validation Tests

    [Test]
    public void TheMotorBikeRecipeIsAcceptedTest()
    {
        var findings = FoamRecipeValidator.Validate(MotorBike());

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void AMissingRecipeIsOneFindingTest()
    {
        var findings = FoamRecipeValidator.Validate(null);

        Assert.That(findings, Is.EqualTo(new[] { "The task carries no recipe." }));
    }

    [Test]
    public void AUtilityOutsideTheAllowListIsRefusedByNameTest()
    {
        var recipe = MotorBike();
        recipe.Steps.Insert(0, new FoamStepData { Utility = "foamyHexMesh" });

        var findings = FoamRecipeValidator.Validate(recipe);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.Contain("foamyHexMesh").And.Contain("allow-list"));
    }

    [Test]
    public void ShellMetacharactersAndEscapingPathsAreRefusedTest()
    {
        var recipe = MotorBike();
        recipe.Steps[1].Arguments = ["-dict", "system/blockMeshDict;rm", "../outside/dict", "/etc/passwd", "\"quoted\""];

        var findings = FoamRecipeValidator.Validate(recipe);

        Assert.That(findings, Has.Count.EqualTo(4));
        Assert.That(findings, Has.Some.Contains("system/blockMeshDict;rm"));
        Assert.That(findings, Has.Some.Contains("../outside/dict"));
        Assert.That(findings, Has.Some.Contains("/etc/passwd"));
        Assert.That(findings, Has.Some.Contains("\"quoted\""));
    }

    [Test]
    public void TheFlagsTheControllerDecidesAreRefusedTest()
    {
        var recipe = MotorBike();
        recipe.Steps[6].Arguments = ["-case", "elsewhere", "-decomposeParDict", "system/other", "-parallel"];

        var findings = FoamRecipeValidator.Validate(recipe);

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.Contains("-case"));
        Assert.That(findings, Has.Some.Contains("-decomposeParDict"));
        Assert.That(findings, Has.Some.Contains("-parallel"));
    }

    [Test]
    public void AParallelStepOnASerialUtilityIsRefusedTest()
    {
        var recipe = MotorBike();
        recipe.Steps[1].Parallel = true; // blockMesh

        var findings = FoamRecipeValidator.Validate(recipe);

        Assert.That(findings, Is.EqualTo(new[] { "Step 2: 'blockMesh' does not run in parallel." }));
    }

    [Test]
    public void TheApplicationMustBeASolverAndMustRunTest()
    {
        var recipe = MotorBike();
        recipe.Application = "blockMesh";
        Assert.That(FoamRecipeValidator.Validate(recipe), Has.Some.Contains("not a solver name"));

        recipe.Application = "pisoFoam";
        Assert.That(FoamRecipeValidator.Validate(recipe), Has.Some.Contains("No step runs the application 'pisoFoam'"));
    }

    [Test]
    public void SolverNamesAreRecognisedByShapeTest()
    {
        Assert.That(FoamAllowList.IsSolverName("simpleFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("rhoPimpleFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("MPPICFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("blockMesh"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("potentialFoam"), Is.False, "a utility of the list, not a solver");
        Assert.That(FoamAllowList.IsSolverName("Foam"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("simple-Foam"), Is.False);
    }

    #endregion
}
