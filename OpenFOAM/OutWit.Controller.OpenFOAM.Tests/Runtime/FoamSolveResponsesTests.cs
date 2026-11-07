using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

/// <summary>
/// Which responses the controller adds to the solve on its own: the ones a
/// post step would read at the latest written time, and no other - not a
/// force (the recipe's includeFunc is its), not one the recipe already
/// includes, not one the case measures itself.
/// </summary>
[TestFixture]
public class FoamSolveResponsesTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-solve-responses");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private void Write(string relative, string text)
    {
        var path = Path.Combine(m_case, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static FoamRecipeData Recipe(params FoamStepData[] steps)
    {
        return new FoamRecipeData { Application = "simpleFoam", Steps = [.. steps] };
    }

    private static FoamExtractionRequestData Request()
    {
        return new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["wall"] },
                new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = ["p"], Operation = "areaAverage" },
                new FoamResponseSpecData { Name = "cellT", Kind = FoamResponseKind.VolumeValue, Fields = ["T"], Operation = "volAverage" },
                new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] },
                new FoamResponseSpecData { Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["U"] }
            ]
        };
    }

    #endregion

    #region Selection Tests

    [Test]
    public void TheStatisticsMinMaxAndProbesAreAddedAndAForceIsNotTest()
    {
        Write("system/controlDict", "application simpleFoam;\n");
        var recipe = Recipe(new FoamStepData { Utility = "blockMesh" }, new FoamStepData { Utility = "simpleFoam" },
            new FoamStepData { Utility = "postProcess", Arguments = ["-func", "inletP", "-latestTime"] });

        var names = FoamSolveResponses.MeasuredInSolve(m_case, recipe, Request());

        Assert.That(names, Is.EqualTo(new[] { "inletP", "cellT", "pRange", "wake" }), "in the request's order; the force is the recipe's own includeFunc to add");
    }

    [Test]
    public void AResponseTheRecipeAlreadyIncludesIsLeftToItsStepTest()
    {
        Write("system/controlDict", "application simpleFoam;\n");
        var recipe = Recipe(new FoamStepData { Utility = "includeFunc", Arguments = ["wake"] }, new FoamStepData { Utility = "simpleFoam" });

        Assert.That(FoamSolveResponses.MeasuredInSolve(m_case, recipe, Request()), Is.EqualTo(new[] { "inletP", "cellT", "pRange" }));
    }

    [Test]
    public void AResponseTheCaseMeasuresItselfIsNotAddedTest()
    {
        // The case's own function object, as a block or as a dictionary of
        // its system/ pulled in by #includeFunc: the node's copy of the case
        // gains no line that would change what the case does.
        Write("system/controlDict", "application simpleFoam;\nfunctions\n{\n    pRange { type fieldMinMax; fields (p); }\n    #includeFunc wake\n}\n");
        Write("system/wake", "type probes;\nfields (U);\nprobeLocations ((0 0 0));\n");
        var recipe = Recipe(new FoamStepData { Utility = "simpleFoam" });

        Assert.That(FoamSolveResponses.MeasuredInSolve(m_case, recipe, Request()), Is.EqualTo(new[] { "inletP", "cellT" }));
        Assert.That(FoamSolveResponses.CaseFunctionObjects(m_case), Is.EquivalentTo(new[] { "pRange", "wake" }));
    }

    [Test]
    public void WithoutASolveOrARequestNothingIsAddedTest()
    {
        Write("system/controlDict", "application simpleFoam;\n");

        Assert.That(FoamSolveResponses.MeasuredInSolve(m_case, Recipe(new FoamStepData { Utility = "simpleFoam", Arguments = ["-postProcess", "-func", "inletP"] }), Request()), Is.Empty,
            "a post step alone is no solve to add to");
        Assert.That(FoamSolveResponses.MeasuredInSolve(m_case, Recipe(new FoamStepData { Utility = "simpleFoam" }), null), Is.Empty);
        Assert.That(FoamSolveResponses.MeasuredInSolve(m_case, Recipe(new FoamStepData { Utility = "simpleFoam" }), new FoamExtractionRequestData()), Is.Empty);
    }

    [Test]
    public void ACaseWithoutAControlDictHasNoFunctionObjectsTest()
    {
        Assert.That(FoamSolveResponses.CaseFunctionObjects(m_case), Is.Empty);
    }

    #endregion
}
