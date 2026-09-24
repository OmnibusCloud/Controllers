using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
[NonParallelizable]
public class FoamKitResolverTests
{
    private static readonly string PROBE = Path.Combine(Path.GetTempPath(), "nowhere", "controller.dll");

    private string? m_previousKitPath;
    private FakeKit? m_kit;

    [SetUp]
    public void Setup()
    {
        m_previousKitPath = Environment.GetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, m_previousKitPath);
        m_kit?.Dispose();
    }

    #region Tools

    private FakeKit RequireKit(params string[] utilities)
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        m_kit = FakeKit.Create(fakeFoam, utilities);
        return m_kit;
    }

    #endregion

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
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, null);
        var probe = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "controller.dll");

        Assert.That(FoamKitResolver.Resolve(probe), Is.Null);
    }

    [Test]
    public void TheOverrideNamesTheKitAndAWrongOneFailsLoudlyTest()
    {
        var fake = RequireKit("blockMesh");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, fake.Root);

        // A kit without a BUILDINFO (this one) is accepted unchecked, with a warning.
        var kit = FoamKitResolver.Resolve(PROBE);

        Assert.That(kit, Is.Not.Null);
        Assert.That(kit!.Root, Is.EqualTo(Path.GetFullPath(fake.Root)));
        Assert.That(kit.Platform, Is.EqualTo("fake"));
        Assert.That(kit.HasExecutable("blockMesh"), Is.True);
        Assert.That(kit.HasExecutable("snappyHexMesh"), Is.False);

        // An override pointing at an empty folder must not fall through to
        // the module's own kit: a configured-but-wrong path fails, visibly.
        var empty = OpenFOAMTestPaths.CreateScratch("foam-empty-kit");
        try
        {
            Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, empty);
            Assert.That(FoamKitResolver.Resolve(PROBE), Is.Null);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(empty);
        }
    }

    [Test]
    public void AKitWithAnUnusableEnvironmentFileIsAbsenceNotAnExceptionTest()
    {
        var root = OpenFOAMTestPaths.CreateScratch("foam-broken-kit");
        try
        {
            File.WriteAllText(Path.Combine(root, FoamKitEnvironment.FILE_NAME), "# no WM_PROJECT_DIR, no PATH\nKIT_PLATFORM=fake\n");
            Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, root);

            Assert.That(FoamKitResolver.Resolve(PROBE), Is.Null);
        }
        finally
        {
            OpenFOAMTestPaths.TryDelete(root);
        }
    }

    [Test]
    public void TheModuleLayoutIsFoundNextToTheAssemblyTest()
    {
        var fake = RequireKit("blockMesh");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, null);

        var runtimeFolder = FoamKitResolver.ResolveCurrentRuntimeFolder();
        if (runtimeFolder == null)
            Assert.Ignore("unsupported platform");

        // openfoam/<runtime-folder>/ beside the controller assembly, as the
        // asset pipeline extracts the archive (ExtractTo=".", the archive
        // carrying openfoam/<platform>/ itself).
        var module = OpenFOAMTestPaths.CreateScratch("foam-module");
        try
        {
            var target = Path.Combine(module, "openfoam", runtimeFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(fake.Root, target);

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

    #region Integrity Tests

    [Test]
    public void ATamperedKitIsRefusedAndAcceptedAgainOnceRepairedTest()
    {
        var fake = RequireKit("blockMesh");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, fake.Root);
        OpenFOAMTestPaths.WriteBuildInfo(fake.Root);

        var envFile = Path.Combine(fake.Root, FoamKitEnvironment.FILE_NAME);
        var original = File.ReadAllText(envFile);
        File.AppendAllText(envFile, "TAMPERED=1\n");

        Assert.That(FoamKitResolver.Resolve(PROBE), Is.Null, "an altered KIT.env refuses the kit");

        // The refusal is not cached: a repaired kit is accepted at the next resolution.
        File.WriteAllText(envFile, original);

        Assert.That(FoamKitResolver.Resolve(PROBE), Is.Not.Null);
    }

    [Test]
    public void ThePstreamTheControllerSwapsIsExemptFromTheIntegrityCheckTest()
    {
        var fake = RequireKit("blockMesh");
        Environment.SetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH, fake.Root);

        // The one file of a kit that is meant to change after unpacking: the
        // Windows Pstream the controller copies the MS-MPI variant over. A
        // kit this small is sampled whole, so without the exemption the
        // swapped file would be a finding.
        var pstream = $"{FakeKit.AppBinRelative}/libPstream.dll";
        fake.AppendEnvironment($"KIT_PSTREAM_TARGET=@KIT@/{pstream}");
        File.WriteAllText(fake.PathOf(pstream), "serial Pstream");
        OpenFOAMTestPaths.WriteBuildInfo(fake.Root);
        File.WriteAllText(fake.PathOf(pstream), "MS-MPI Pstream, copied over by the controller");

        Assert.That(FoamKitResolver.Resolve(PROBE), Is.Not.Null);
    }

    #endregion
}
