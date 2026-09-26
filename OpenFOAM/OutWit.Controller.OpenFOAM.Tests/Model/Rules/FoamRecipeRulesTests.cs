using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamRecipeRulesTests
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
        var findings = FoamRecipeRules.Validate(MotorBike());

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void AMissingRecipeIsOneFindingTest()
    {
        var findings = FoamRecipeRules.Validate(null);

        Assert.That(findings, Is.EqualTo(new[] { "The task carries no recipe." }));
    }

    [Test]
    public void AUtilityOutsideTheAllowListIsRefusedByNameTest()
    {
        var recipe = MotorBike();
        recipe.Steps.Insert(0, new FoamStepData { Utility = "foamyHexMesh" });

        var findings = FoamRecipeRules.Validate(recipe);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.Contain("foamyHexMesh").And.Contain("allow-list"));
    }

    [Test]
    public void ShellMetacharactersAndEscapingPathsAreRefusedTest()
    {
        var recipe = MotorBike();
        recipe.Steps[1].Arguments = ["-dict", "system/blockMeshDict;rm", "../outside/dict", "/etc/passwd", "\"quoted\""];

        var findings = FoamRecipeRules.Validate(recipe);

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

        var findings = FoamRecipeRules.Validate(recipe);

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

        var findings = FoamRecipeRules.Validate(recipe);

        Assert.That(findings, Is.EqualTo(new[] { "Step 2: 'blockMesh' does not run in parallel." }));
    }

    [Test]
    public void TheApplicationMustBeASolverAndMustRunTest()
    {
        var recipe = MotorBike();
        recipe.Application = "blockMesh";
        Assert.That(FoamRecipeRules.Validate(recipe), Has.Some.Contains("not a solver name"));

        recipe.Application = "pisoFoam";
        Assert.That(FoamRecipeRules.Validate(recipe), Has.Some.Contains("No step runs the application 'pisoFoam'"));
    }

    [Test]
    public void NegativeNumbersAreValuesNotFlagsTest()
    {
        var recipe = MotorBike();
        recipe.Steps[9].Arguments = ["-func", "forceCoeffs", "-time", "-1", "-scale", "-1.5e-3"];

        Assert.That(FoamRecipeRules.Validate(recipe), Is.Empty);

        recipe.Steps[9].Arguments = ["-time", "-1abc"];
        Assert.That(FoamRecipeRules.Validate(recipe), Has.Exactly(1).Items.And.Some.Contains("-1abc"));
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

        var findings = FoamRecipeRules.Validate(recipe, kit.HasExecutable);

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.EqualTo("The kit has no solver 'pisoFoam'."));
        Assert.That(findings, Has.Some.EqualTo("Step 2: the kit has no 'checkMesh'."));
        Assert.That(findings, Has.Some.EqualTo("Step 3: the kit has no 'pisoFoam'."));
    }

    [Test]
    public void ARecipeLongerThanTheCapIsRefusedTest()
    {
        var recipe = MotorBike();
        while (recipe.Steps.Count <= FoamRecipeRules.MAX_STEPS)
            recipe.Steps.Insert(0, new FoamStepData { Utility = "checkMesh" });

        Assert.That(FoamRecipeRules.Validate(recipe), Has.Some.Contains($"at most {FoamRecipeRules.MAX_STEPS}"));
    }

    #endregion

    #region Step Tests

    [Test]
    public void OneStepIsJudgedInTheSameWordsUnderTheCallersNameTest()
    {
        var allowed = FoamRecipeRules.ValidateStep(new FoamStepData { Utility = "checkMesh", Arguments = ["-writeFields", "(nonOrthoAngle)", "-constant"], Parallel = true }, "Allrun:31");
        var outside = FoamRecipeRules.ValidateStep(new FoamStepData { Utility = "redistributePar", Arguments = ["-decompose"] }, "Allrun:12");
        var flag = FoamRecipeRules.ValidateStep(new FoamStepData { Utility = "decomposePar", Arguments = ["-decomposeParDict", "system/decomposeParDict.6"] }, "Allrun:14");
        var missing = FoamRecipeRules.ValidateStep(new FoamStepData { Utility = "blockMesh" }, "Allrun:9", _ => false);

        Assert.That(allowed, Is.Empty);
        Assert.That(outside, Is.EqualTo(new[] { "Allrun:12: 'redistributePar' is not on the allow-list." }));
        Assert.That(flag, Is.EqualTo(new[] { "Allrun:14 (decomposePar): '-decomposeParDict' is decided by the controller and may not be given." }));
        Assert.That(missing, Is.EqualTo(new[] { "Allrun:9: the kit has no 'blockMesh'." }));
    }

    #endregion

    #region Built-In Tests

    [Test]
    public void MeshingOnTheDecomposedCaseThenRestoringTheFieldsIsAcceptedWithoutAKitExecutableTest()
    {
        // The motorBike tutorial's own order: decompose the background mesh, snap in parallel, put the initial fields into the processors.
        var recipe = new FoamRecipeData
        {
            Application = "simpleFoam",
            Steps =
            [
                new FoamStepData { Utility = "blockMesh" },
                new FoamStepData { Utility = "decomposePar" },
                new FoamStepData { Utility = "snappyHexMesh", Arguments = ["-overwrite"], Parallel = true },
                new FoamStepData { Utility = "restore0Dir", Arguments = ["-processor"] },
                new FoamStepData { Utility = "simpleFoam", Parallel = true },
                new FoamStepData { Utility = "reconstructParMesh", Arguments = ["-constant"] },
                new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] }
            ]
        };

        Assert.That(FoamRecipeRules.Validate(recipe, name => name != "restore0Dir"), Is.Empty, "the controller's own step needs nothing from the kit");
    }

    [Test]
    public void OnlyTheProcessorFormAfterADecompositionIsAcceptedTest()
    {
        FoamRecipeData Recipe(params FoamStepData[] steps) => new() { Application = "simpleFoam", Steps = [.. steps, new FoamStepData { Utility = "simpleFoam" }] };
        var decompose = new FoamStepData { Utility = "decomposePar" };

        var plain = FoamRecipeRules.Validate(Recipe(decompose, new FoamStepData { Utility = "restore0Dir" }));
        var all = FoamRecipeRules.Validate(Recipe(decompose, new FoamStepData { Utility = "restore0Dir", Arguments = ["-all"] }));
        var underMpi = FoamRecipeRules.Validate(Recipe(decompose, new FoamStepData { Utility = "restore0Dir", Arguments = ["-processor"], Parallel = true }));
        var undecomposed = FoamRecipeRules.Validate(Recipe(new FoamStepData { Utility = "restore0Dir", Arguments = ["-processor"] }));

        Assert.That(plain, Is.EqualTo(new[] { "Step 2 (restore0Dir): the only form is 'restore0Dir -processor'; a serial run's initial fields travel in 0/." }));
        Assert.That(all, Is.EqualTo(plain));
        Assert.That(underMpi, Is.EqualTo(new[] { "Step 2: 'restore0Dir' is done by the controller itself, never under MPI." }));
        Assert.That(undecomposed, Is.EqualTo(new[] { "Step 1: restore0Dir -processor needs a decomposePar step before it." }));
    }

    #endregion
}
