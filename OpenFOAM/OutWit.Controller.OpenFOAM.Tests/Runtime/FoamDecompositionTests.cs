using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamDecompositionTests
{
    #region Rank Tests

    [Test]
    public void ARequestedRankCountIsTakenAsIsTest()
    {
        Assert.That(FoamDecomposition.Ranks(1), Is.EqualTo(1));
        Assert.That(FoamDecomposition.Ranks(3), Is.EqualTo(3));
        Assert.That(FoamDecomposition.Ranks(64), Is.EqualTo(64), "an explicit request is the user's decision, not capped");
    }

    [Test]
    public void AllCoresMeansTheMachineCappedTest()
    {
        var ranks = FoamDecomposition.Ranks(0);

        Assert.That(ranks, Is.InRange(1, FoamDecomposition.MAX_DEFAULT_RANKS));
        Assert.That(ranks, Is.EqualTo(Math.Clamp(Environment.ProcessorCount, 1, FoamDecomposition.MAX_DEFAULT_RANKS)));
        Assert.That(FoamDecomposition.Ranks(-2), Is.EqualTo(ranks), "a negative request reads as all cores");
    }

    #endregion

    #region Dictionary Tests

    [Test]
    public void TheDictionaryNamesScotchAndTheRanksTest()
    {
        var text = FoamDecomposition.Dictionary(6);

        Assert.That(text, Does.StartWith("FoamFile"));
        Assert.That(text, Does.Contain("object      decomposeParDict;"));
        Assert.That(text, Does.Contain("numberOfSubdomains 6;").And.Contain($"method {FoamDecomposition.METHOD};"));
        Assert.That(FoamDecomposition.METHOD, Is.EqualTo("scotch"), "the one method every kit builds");
    }

    [Test]
    public void TheDictionaryIsWrittenOverTheCasesOwnTest()
    {
        var caseDirectory = OpenFOAMTestPaths.CreateScratch("foam-decompose");
        try
        {
            Directory.CreateDirectory(Path.Combine(caseDirectory, "system"));
            File.WriteAllText(Path.Combine(caseDirectory, "system", "decomposeParDict"), "numberOfSubdomains 128;\nmethod hierarchical;\n");

            var path = FoamDecomposition.WriteDecomposeParDict(caseDirectory, 4);

            Assert.That(path, Is.EqualTo(Path.Combine(caseDirectory, "system", "decomposeParDict")));
            var text = File.ReadAllText(path);
            Assert.That(text, Does.Contain("numberOfSubdomains 4;").And.Contain("method scotch;"));
            Assert.That(text, Does.Not.Contain("hierarchical"), "the user's machine count is not the node's");
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(caseDirectory);
        }
    }

    [Test]
    public void TheDictionaryIsWrittenWhenTheCaseHasNoSystemDirectoryYetTest()
    {
        var caseDirectory = OpenFOAMTestPaths.CreateScratch("foam-decompose");
        try
        {
            var path = FoamDecomposition.WriteDecomposeParDict(caseDirectory, 2);

            Assert.That(File.Exists(path), Is.True);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(caseDirectory);
        }
    }

    #endregion
}
