using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamScratchPathTests
{
    #region Path Tests

    [Test]
    public void APathWithoutASpaceIsReturnedAsItIsTest()
    {
        var path = OpenFOAMTestPaths.CreateScratch("foam-path");
        try
        {
            Assert.That(FoamScratchPath.WithoutSpaces(path), Is.EqualTo(path));
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(path);
        }
    }

    [Test]
    public void APathWithASpaceGetsItsShortFormOnWindowsAndNothingElsewhereTest()
    {
        var parent = OpenFOAMTestPaths.CreateScratch("foam-path");
        var spaced = Path.Combine(parent, "with space");
        Directory.CreateDirectory(spaced);
        try
        {
            var result = FoamScratchPath.WithoutSpaces(spaced);

            if (!OperatingSystem.IsWindows())
            {
                Assert.That(result, Is.Null);
                return;
            }

            if (result == null)
                Assert.Ignore("this volume keeps no 8.3 names; the rule then refuses the path, as designed");

            Assert.That(result, Does.Not.Contain(" "));
            Assert.That(Directory.Exists(result), Is.True);

            // The short form names the same directory: a file made through
            // one path is there through the other.
            File.WriteAllText(Path.Combine(result, "marker"), "x");
            Assert.That(File.Exists(Path.Combine(spaced, "marker")), Is.True);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(parent);
        }
    }

    #endregion
}
