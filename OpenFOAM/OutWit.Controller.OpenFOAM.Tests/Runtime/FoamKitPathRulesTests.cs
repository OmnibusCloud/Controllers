using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

/// <summary>
/// Where the kit may be installed: no space in its path off Windows (OpenFOAM
/// dies on its own executable path otherwise), and on Windows no file past
/// 259 characters (the kit's binaries are not long-path aware).
/// </summary>
[TestFixture]
public class FoamKitPathRulesTests
{
    #region Check Tests

    [Test]
    public void AKitAtAFitPathIsAcceptedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FoamKitPathRules.Check("/Users/Shared/OmnibusCloud/Controllers/7f33/openfoam/macos-arm64", false, 110), Is.Null);
            Assert.That(FoamKitPathRules.Check(@"C:\ProgramData\OmnibusCloud\Controllers\7f33\openfoam\windows-x64", true, 110), Is.Null);
        });
    }

    [Test]
    public void AKitUnderASpaceIsRefusedOffWindowsOnlyTest()
    {
        const string root = "/Users/node/Library/Application Support/OmnibusCloud/Controllers/7f33/openfoam/macos-arm64";

        Assert.Multiple(() =>
        {
            Assert.That(FoamKitPathRules.Check(root, false, 110), Does.Contain("a path with a space").And.Contain(root));
            Assert.That(FoamKitPathRules.Check(@"C:\Users\John Smith\Controllers\openfoam\windows-x64", true, 110), Is.Null,
                "the Windows build accepts a space");
            Assert.That(FoamKitPathRules.Check("/Users/node/kits\u00A0v2606/openfoam/macos-arm64", false, 110), Does.Contain("a path with a space"),
                "any whitespace, not only the ASCII space");
        });
    }

    [Test]
    public void AKitTooDeepForWindowsIsRefusedWithTheLengthTest()
    {
        var root = @"C:\ProgramData\OmnibusCloud\Controllers\_isolated\" + string.Join("_", Enumerable.Repeat(new string('a', 32), 5)) + @"\7f33\openfoam\windows-x64";
        var deepest = root.Length + 1 + 110;

        Assert.Multiple(() =>
        {
            Assert.That(FoamKitPathRules.Check(root, true, 110), Does.Contain(deepest.ToString()).And.Contain(FoamKitPathRules.MAX_WINDOWS_PATH.ToString()));
            Assert.That(FoamKitPathRules.Check(root.Replace('\\', '/'), false, 110), Is.Null, "no length limit off Windows");
        });
    }

    [Test]
    public void TheLimitIsTheLastCharacterAWindowsToolCanOpenTest()
    {
        var root = @"C:\" + new string('r', 100);
        var fits = FoamKitPathRules.MAX_WINDOWS_PATH - root.Length - 1;

        Assert.Multiple(() =>
        {
            Assert.That(FoamKitPathRules.Check(root, true, fits), Is.Null);
            Assert.That(FoamKitPathRules.Check(root, true, fits + 1), Is.Not.Null);
        });
    }

    #endregion

    #region Measure Tests

    [Test]
    public void TheDeepestRelativePathIsMeasuredTest()
    {
        var root = OpenFOAMTestPaths.CreateScratch("foam-deep");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "etc", "caseDicts"));
            File.WriteAllText(Path.Combine(root, "KIT.env"), "x");
            File.WriteAllText(Path.Combine(root, "etc", "caseDicts", "streamlines.cfg"), "x");

            var expected = Path.Combine("etc", "caseDicts", "streamlines.cfg").Length;

            Assert.That(FoamKitPathRules.DeepestRelativePath(root), Is.EqualTo(expected));
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(root);
        }
    }

    #endregion
}
