using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamAllowListTests
{
    #region Utility Tests

    [Test]
    public void TheUtilitiesOfVersionOneAreKnownByNameTest()
    {
        foreach (var utility in new[] { "blockMesh", "snappyHexMesh", "surfaceFeatureExtract", "decomposePar", "reconstructPar", "reconstructParMesh", "checkMesh", "postProcess", "potentialFoam", "foamDictionary" })
            Assert.That(FoamAllowList.IsUtility(utility), Is.True, utility);

        foreach (var outside in new[] { "foamyHexMesh", "paraFoam", "foamToVTK", "redistributePar", "changeDictionary", "cumulativeDisplacement", "bash", "sh", "python", "BlockMesh", "blockmesh", "" })
            Assert.That(FoamAllowList.IsUtility(outside), Is.False, outside);
    }

    [Test]
    public void TheMeshSetAndInitialisationUtilitiesJoinedInOnePointOneTest()
    {
        // Mesh, set and region utilities the tutorials use around topoSet and
        // snappyHexMesh, and the two initialisations beside setFields - each a
        // deterministic operation on the case's own files.
        foreach (var utility in new[]
                 {
                     "setsToZones", "subsetMesh", "splitMeshRegions", "mergeMeshes", "mergeOrSplitBaffles", "extrudeToRegionMesh",
                     "refineHexMesh", "collapseEdges", "extrude2DMesh", "setAlphaField", "makeFaMesh"
                 })
            Assert.That(FoamAllowList.IsUtility(utility), Is.True, utility);

        // Parallel only where the tutorials run them in parallel; the others accept -parallel but are allowed serially.
        Assert.That(FoamAllowList.IsParallelCapable("splitMeshRegions"), Is.True);
        Assert.That(FoamAllowList.IsParallelCapable("makeFaMesh"), Is.True);
        foreach (var serial in new[] { "setsToZones", "subsetMesh", "mergeMeshes", "mergeOrSplitBaffles", "extrudeToRegionMesh", "refineHexMesh", "collapseEdges", "extrude2DMesh", "setAlphaField" })
            Assert.That(FoamAllowList.IsParallelCapable(serial), Is.False, serial);
    }

    [Test]
    public void TheListIsExactlyWhatTheDocumentationPublishesTest()
    {
        // The list is a contract with the initiator's preflight and with the
        // supported-inputs document; a change here is a change there.
        Assert.That(FoamAllowList.UTILITIES, Has.Count.EqualTo(30));
        Assert.That(FoamAllowList.UTILITIES, Is.Unique);
        Assert.That(FoamAllowList.PARALLEL_CAPABLE_UTILITIES, Is.SubsetOf(FoamAllowList.UTILITIES), "a parallel-capable name is first of all an allow-listed one");
    }

    #endregion

    #region Solver Tests

    [Test]
    public void SolverNamesAreRecognisedByShapeTest()
    {
        Assert.That(FoamAllowList.IsSolverName("simpleFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("rhoPimpleFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("MPPICFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("interFoam"), Is.True);
        Assert.That(FoamAllowList.IsSolverName("blockMesh"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("potentialFoam"), Is.False, "a utility of the list, not a solver");
        Assert.That(FoamAllowList.IsSolverName("Foam"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("simple-Foam"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("simpleFoam;rm"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("../simpleFoam"), Is.False);
        Assert.That(FoamAllowList.IsSolverName("simplefoam"), Is.False, "the suffix is case-sensitive, as the executables are");
    }

    #endregion

    #region Parallel Tests

    [Test]
    public void SolversAndTheMarkedUtilitiesRunInParallelTest()
    {
        Assert.That(FoamAllowList.IsParallelCapable("simpleFoam"), Is.True);
        Assert.That(FoamAllowList.IsParallelCapable("snappyHexMesh"), Is.True);
        Assert.That(FoamAllowList.IsParallelCapable("checkMesh"), Is.True);
        Assert.That(FoamAllowList.IsParallelCapable("potentialFoam"), Is.True);
        Assert.That(FoamAllowList.IsParallelCapable("blockMesh"), Is.False);
        Assert.That(FoamAllowList.IsParallelCapable("decomposePar"), Is.False);
        Assert.That(FoamAllowList.IsParallelCapable("reconstructPar"), Is.False);
        Assert.That(FoamAllowList.IsParallelCapable("foamyHexMesh"), Is.False, "outside the list, parallel or not");
    }

    #endregion

    #region Flag Tests

    [Test]
    public void TheFlagsTheControllerDecidesAreForbiddenTest()
    {
        foreach (var flag in new[] { "-case", "-decomposeParDict", "-fileHandler", "-hostRoots", "-roots", "-lib", "-libs" })
            Assert.That(FoamAllowList.IsForbiddenFlag(flag), Is.True, flag);

        foreach (var flag in new[] { "-latestTime", "-overwrite", "-func", "-dict", "-parallel", "-writephi", "-noFunctionObjects" })
            Assert.That(FoamAllowList.IsForbiddenFlag(flag), Is.False, flag);
    }

    #endregion

    #region Built-In Tests

    [Test]
    public void TheControllersOwnStepIsNeitherAUtilityNorASolverTest()
    {
        Assert.That(FoamAllowList.BUILT_IN_STEPS, Is.EqualTo(new[] { FoamAllowList.RESTORE_INITIAL_FIELDS }));
        Assert.That(FoamAllowList.RESTORE_INITIAL_FIELDS, Is.EqualTo("restore0Dir"), "the name OpenFOAM's RunFunctions give it, so a recipe reads like the Allrun it came from");
        Assert.That(FoamAllowList.IsBuiltIn("restore0Dir"), Is.True);
        Assert.That(FoamAllowList.IsBuiltIn("restore0dir"), Is.False);
        Assert.That(FoamAllowList.IsBuiltIn("blockMesh"), Is.False);

        Assert.That(FoamAllowList.IsUtility("restore0Dir"), Is.False, "no kit executable carries the name");
        Assert.That(FoamAllowList.IsSolverName("restore0Dir"), Is.False);
        Assert.That(FoamAllowList.IsParallelCapable("restore0Dir"), Is.False, "the controller does it once, not under MPI");
        Assert.That(FoamAllowList.UTILITIES, Has.No.Member("restore0Dir"));
    }

    #endregion
}
