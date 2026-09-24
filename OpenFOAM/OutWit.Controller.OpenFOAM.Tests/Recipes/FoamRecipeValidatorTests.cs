using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Recipes;
using OutWit.Controller.OpenFOAM.Tests.Utils;

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
    public void NegativeNumbersAreValuesNotFlagsTest()
    {
        var recipe = MotorBike();
        recipe.Steps[9].Arguments = ["-func", "forceCoeffs", "-time", "-1", "-scale", "-1.5e-3"];

        Assert.That(FoamRecipeValidator.Validate(recipe), Is.Empty);

        recipe.Steps[9].Arguments = ["-time", "-1abc"];
        Assert.That(FoamRecipeValidator.Validate(recipe), Has.Exactly(1).Items.And.Some.Contains("-1abc"));
    }

    [Test]
    public void TheKitDecidesWhetherAnAllowedStepCanRunTest()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        using var fake = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam");
        var kit = fake.Resolve();
        var recipe = new FoamRecipeData
        {
            Application = "pisoFoam",
            Steps = [new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "checkMesh" }, new FoamStepData { Utility = "pisoFoam" }]
        };

        var findings = FoamRecipeValidator.Validate(recipe, kit);

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.EqualTo("The kit has no solver 'pisoFoam'."));
        Assert.That(findings, Has.Some.EqualTo("Step 2: the kit has no 'checkMesh'."));
        Assert.That(findings, Has.Some.EqualTo("Step 3: the kit has no 'pisoFoam'."));
    }

    [Test]
    public void ARecipeLongerThanTheCapIsRefusedTest()
    {
        var recipe = MotorBike();
        while (recipe.Steps.Count <= FoamRecipeValidator.MAX_STEPS)
            recipe.Steps.Insert(0, new FoamStepData { Utility = "checkMesh" });

        Assert.That(FoamRecipeValidator.Validate(recipe), Has.Some.Contains($"at most {FoamRecipeValidator.MAX_STEPS}"));
    }

    #endregion
}
