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

        foreach (var outside in new[] { "foamyHexMesh", "paraFoam", "foamToVTK", "bash", "sh", "python", "BlockMesh", "blockmesh", "" })
            Assert.That(FoamAllowList.IsUtility(outside), Is.False, outside);
    }

    [Test]
    public void TheListIsExactlyWhatTheDocumentationPublishesTest()
    {
        // The list is a contract with the initiator's preflight and with the
        // supported-inputs document; a change here is a change there.
        Assert.That(FoamAllowList.UTILITIES, Has.Count.EqualTo(19));
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
}
