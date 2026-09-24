using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
[NonParallelizable]
public class FoamKitResolverTests
{
    private FakeKit? m_kit;

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, null);
        m_kit?.Dispose();
    }

    #region Resolution Tests

    [Test]
    public void CurrentRuntimeFolderIsKnownOnSupportedPlatformsTest()
    {
        // The build/test fleet runs only on the three supported platforms, so
        // the mapping must resolve here - and to the asset extraction
        // vocabulary (windows-x64/linux-x64/macos-arm64), not to RIDs.
        var folder = FoamKitResolver.ResolveCurrentRuntimeFolder();

        Assert.That(folder, Is.Not.Null);
        Assert.That(folder, Is.AnyOf("windows-x64", "linux-x64", "macos-arm64"));
    }

    [Test]
    public void ResolveReturnsNullWithoutABundledKitTest()
    {
        // A bare assembly path with no openfoam/ folder next to it: the
        // resolver reports absence instead of throwing - the adapter turns
        // that into its own loud error.
        var probe = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "controller.dll");

        Assert.That(FoamKitResolver.Resolve(probe), Is.Null);
    }

    [Test]
    public void TheOverrideNamesTheKitAndAWrongOneFailsLoudlyTest()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_kit.Root);

        var kit = FoamKitResolver.Resolve(Path.Combine(Path.GetTempPath(), "nowhere", "controller.dll"));

        Assert.That(kit, Is.Not.Null);
        Assert.That(kit!.Root, Is.EqualTo(Path.GetFullPath(m_kit.Root)));
        Assert.That(kit.Platform, Is.EqualTo("fake"));
        Assert.That(kit.HasExecutable("blockMesh"), Is.True);
        Assert.That(kit.HasExecutable("snappyHexMesh"), Is.False);

        // An override pointing at an empty folder must not fall through to
        // the module's own kit: a configured-but-wrong path fails, visibly.
        var empty = OpenFOAMTestPaths.CreateScratch("foam-empty-kit");
        try
        {
            Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, empty);
            Assert.That(FoamKitResolver.Resolve(Path.Combine(Path.GetTempPath(), "nowhere", "controller.dll")), Is.Null);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(empty);
        }
    }

    [Test]
    public void TheModuleLayoutIsFoundNextToTheAssemblyTest()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        var runtimeFolder = FoamKitResolver.ResolveCurrentRuntimeFolder();
        if (runtimeFolder == null)
            Assert.Ignore("unsupported platform");

        // openfoam/<runtime-folder>/ beside the controller assembly, as the
        // asset pipeline extracts the archive (ExtractTo=".", the archive
        // carrying openfoam/<platform>/ itself).
        m_kit = FakeKit.Create(fakeFoam, "blockMesh");
        var module = OpenFOAMTestPaths.CreateScratch("foam-module");
        try
        {
            var target = Path.Combine(module, "openfoam", runtimeFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(m_kit.Root, target);

            var kit = FoamKitResolver.Resolve(Path.Combine(module, "OutWit.Controller.OpenFOAM.dll"));

            Assert.That(kit, Is.Not.Null);
            Assert.That(kit!.Root, Is.EqualTo(Path.GetFullPath(target)));
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(module);
        }
    }

    #endregion
}
