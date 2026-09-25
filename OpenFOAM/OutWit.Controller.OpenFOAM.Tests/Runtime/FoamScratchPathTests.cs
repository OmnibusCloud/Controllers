using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Mock;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Interfaces;

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

    #region Scratch Tests

    [Test]
    public void TheScratchIsAScopeOfTheHostsTempFolderTest()
    {
        var root = OpenFOAMTestPaths.CreateScratch("foam-temp");
        try
        {
            var scratch = FoamScratchPath.CreateScratch(new WitTempStorageDefault(root), "openfoam");

            Assert.That(Path.GetFullPath(scratch.ScopePath), Does.StartWith(Path.Combine(Path.GetFullPath(root), "openfoam") + Path.DirectorySeparatorChar),
                "the host's folder, under the controller's label - never a folder of the controller's own");
            Assert.That(scratch.UsablePath, Is.EqualTo(scratch.ScopePath), "a path without whitespace is used as it is");
            Assert.That(Directory.Exists(Path.Combine(scratch.UsablePath, "home")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(scratch.UsablePath, "tmp")), Is.True);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(root);
        }
    }

    [Test]
    public void AScratchGoesBackToTheTempFolderThroughItsScopeTest()
    {
        // On Windows a folder with a space, so the scratch runs from its 8.3
        // form while the scope keeps its own name; elsewhere a plain folder.
        var parent = OpenFOAMTestPaths.CreateScratch("foam-temp");
        var root = Path.Combine(parent, OperatingSystem.IsWindows() ? "with space" : "plain");
        try
        {
            var storage = new RecordingTempStorage(root);
            FoamScratch scratch;
            try
            {
                scratch = FoamScratchPath.CreateScratch(storage, "openfoam");
            }
            catch (InvalidOperationException)
            {
                Assert.Ignore("this volume keeps no 8.3 names; the refusal is tested elsewhere");
                return;
            }

            File.WriteAllText(Path.Combine(scratch.UsablePath, "tmp", "left.txt"), "x");

            Assert.That(FoamScratchPath.Delete(storage, scratch), Is.True);
            Assert.That(storage.DeletedScopes, Is.EqualTo(new[] { scratch.ScopePath }),
                "the scope the temp folder handed out goes back - not the short form OpenFOAM was given");
            Assert.That(Directory.Exists(scratch.ScopePath), Is.False);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(parent);
        }
    }

    [Test]
    public void ATempFolderWithASpaceIsRefusedByNameOffWindowsTest()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Windows runs from the 8.3 form of such a folder (see the path tests)");

        var parent = OpenFOAMTestPaths.CreateScratch("foam-temp");
        var root = Path.Combine(parent, "Application Support");
        try
        {
            var storage = new WitTempStorageDefault(root);

            var refusal = Assert.Throws<InvalidOperationException>(() => FoamScratchPath.CreateScratch(storage, "openfoam"));

            Assert.That(refusal!.Message, Does.Contain(root).And.Contain("Settings"));
            Assert.That(Directory.GetDirectories(Path.Combine(root, "openfoam")), Is.Empty, "the refused scope is not left behind");
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(parent);
        }
    }

    #endregion

    #region Depth Tests

    [Test]
    public void TheReserveLeavesRoomForTheDeepestPathsOfADecomposedCaseTest()
    {
        var deepest = new[]
        {
            @"\case\processor127\0.000123456789\uniform\functionObjects\functionObjectProperties",
            @"\case\constant\extendedFeatureEdgeMesh\motorBike.extendedFeatureEdgeMesh",
            @"\case\postProcessing\forceCoeffs_object\0.000123456789\coefficient.dat"
        };

        Assert.Multiple(() =>
        {
            Assert.That(FoamScratchPath.MAX_WINDOWS_SCRATCH, Is.EqualTo(FoamKitPathRules.MAX_WINDOWS_PATH - FoamScratchPath.WINDOWS_CASE_RESERVE),
                "the budget is derived from what Windows tools can open, not chosen");
            Assert.That(deepest.Max(path => path.Length), Is.LessThan(FoamScratchPath.WINDOWS_CASE_RESERVE));
        });
    }

    [Test]
    public void AScratchTooDeepForWindowsIsRefusedWithItsLengthTest()
    {
        var fits = @"C:\" + new string('t', FoamScratchPath.MAX_WINDOWS_SCRATCH - 3);
        var deep = fits + "t";

        Assert.Multiple(() =>
        {
            Assert.That(FoamScratchPath.CheckDepth(fits, true, @"C:\temp"), Is.Null, "the budget itself fits");
            Assert.That(FoamScratchPath.CheckDepth(deep, true, @"C:\temp"),
                Does.Contain(deep.Length.ToString()).And.Contain(FoamScratchPath.MAX_WINDOWS_SCRATCH.ToString()).And.Contain(@"C:\temp").And.Contain("Settings"));
            Assert.That(FoamScratchPath.CheckDepth("/tmp/" + deep, false, "/tmp"), Is.Null, "no limit that matters off Windows");
        });
    }

    [Test]
    public void ATempFolderTooDeepForWindowsIsRefusedAndLeavesNothingTest()
    {
        // Segments of eight characters are their own 8.3 names: the short
        // form of this folder is no shorter, so the depth decides.
        var parent = OpenFOAMTestPaths.CreateScratch("foam-temp");
        var root = Path.Combine(new[] { parent }.Concat(Enumerable.Repeat("deepdeep", 16)).ToArray());
        try
        {
            var storage = new RecordingTempStorage(root);

            if (!OperatingSystem.IsWindows())
            {
                var scratch = FoamScratchPath.CreateScratch(storage, "openfoam");
                Assert.That(Directory.Exists(scratch.UsablePath), Is.True, "no limit that matters off Windows");
                return;
            }

            var refusal = Assert.Throws<InvalidOperationException>(() => FoamScratchPath.CreateScratch(storage, "openfoam"));

            Assert.That(refusal!.Message, Does.Contain("too deep").And.Contain(root));
            Assert.That(storage.DeletedScopes, Is.EqualTo(storage.CreatedScopes), "the refused scope goes back");
            Assert.That(storage.CreatedScopes.Where(Directory.Exists), Is.Empty, "the refused scope is not left behind");
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(parent);
        }
    }

    #endregion
}
