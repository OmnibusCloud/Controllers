using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamKitTests
{
    private string m_root = null!;

    [SetUp]
    public void Setup()
    {
        m_root = OpenFOAMTestPaths.CreateScratch("foam-kit");
        Directory.CreateDirectory(Path.Combine(m_root, "OpenFOAM-v2606", "platforms", "x", "bin"));
        Directory.CreateDirectory(Path.Combine(m_root, "ThirdParty-v2606", "platforms", "x", "openmpi-4.1.8", "bin"));
        File.WriteAllText(Path.Combine(m_root, "OpenFOAM-v2606", "platforms", "x", "bin", OperatingSystem.IsWindows() ? "simpleFoam.exe" : "simpleFoam"), "x");
        File.WriteAllText(Path.Combine(m_root, "KIT.env"),
            "PATH=@KIT@/ThirdParty-v2606/platforms/x/openmpi-4.1.8/bin:@KIT@/OpenFOAM-v2606/platforms/x/bin\n" +
            "WM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n" +
            "FOAM_APPBIN=@KIT@/OpenFOAM-v2606/platforms/x/bin\n" +
            "HOME=@SCRATCH@/home\n" +
            "KIT_PLATFORM=test\n");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_root);
    }

    #region Tools

    private FoamKit Kit()
    {
        return new FoamKit(m_root, FoamKitEnvironment.Load(Path.Combine(m_root, "KIT.env")));
    }

    #endregion

    #region Kit Tests

    [Test]
    public void ExecutablesAreResolvedInTheAppBinWithThePlatformsExtensionTest()
    {
        var kit = Kit();

        Assert.That(kit.Platform, Is.EqualTo("test"));
        Assert.That(kit.AppBin, Is.EqualTo(Path.Combine(Path.GetFullPath(m_root), "OpenFOAM-v2606", "platforms", "x", "bin").Replace('\\', '/')).Or.EqualTo(Path.Combine(Path.GetFullPath(m_root), "OpenFOAM-v2606", "platforms", "x", "bin")));
        Assert.That(kit.HasExecutable("simpleFoam"), Is.True);
        Assert.That(kit.HasExecutable("blockMesh"), Is.False);
        Assert.That(Path.GetFileName(kit.ExecutablePath("blockMesh")), Is.EqualTo(OperatingSystem.IsWindows() ? "blockMesh.exe" : "blockMesh"));
    }

    [Test]
    public void TheMpiLauncherIsTheKitsMpirunOnLinuxAndMacOSAndTheNodesMpiexecOnWindowsTest()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: the launcher is the node's MS-MPI or nothing; the kit never carries one.
            var expected = Environment.GetEnvironmentVariable("MSMPI_BIN", EnvironmentVariableTarget.Machine) is { Length: > 0 } bin
                           && File.Exists(Path.Combine(bin, "mpiexec.exe"));
            Assert.That(Kit().SupportsParallel, Is.EqualTo(expected));
            return;
        }

        Assert.That(Kit().SupportsParallel, Is.False, "no mpirun in the kit yet");

        File.WriteAllText(Path.Combine(m_root, "ThirdParty-v2606", "platforms", "x", "openmpi-4.1.8", "bin", "mpirun"), "#!/bin/sh\n");
        var kit = Kit();

        Assert.That(kit.SupportsParallel, Is.True);
        Assert.That(kit.MpiLauncher, Does.EndWith("/openmpi-4.1.8/bin/mpirun"));
    }

    [Test]
    public void TheEnvironmentForATaskSubstitutesTheScratchAndAppendsTheSystemPathTest()
    {
        var scratch = Path.Combine(m_root, "scratch");
        var environment = Kit().EnvironmentFor(scratch);

        Assert.That(environment["HOME"], Is.EqualTo(Path.Combine(scratch, "home").Replace('\\', '/')));
        Assert.That(environment["PATH"], Does.EndWith(FoamKitEnvironment.SystemPath()));
        Assert.That(environment["WM_PROJECT_DIR"], Does.Not.Contain("@KIT@"));
        if (OperatingSystem.IsWindows())
            Assert.That(environment.ContainsKey("SystemRoot"), Is.True);
    }

    [Test]
    public void AKitEnvWithoutTheAppBinIsRefusedTest()
    {
        File.WriteAllText(Path.Combine(m_root, "KIT.env"), "PATH=@KIT@/bin\nWM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n");

        Assert.That(() => Kit(), Throws.TypeOf<InvalidDataException>());
    }

    #endregion
}
