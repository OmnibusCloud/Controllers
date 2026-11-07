using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamKitEnvironmentTests
{
    private const string LINUX_KIT_ENV =
        "# OpenFOAM v2606 kit, linux-x64 (linux64GccDPInt32Opt).\n" +
        "# Set every line on the solver process.\n" +
        "FOAM_APPBIN=@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/bin\n" +
        "FOAM_LIBBIN=@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/lib\n" +
        "FOAM_MPI=openmpi-4.1.8\n" +
        "LD_LIBRARY_PATH=@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/lib/openmpi-4.1.8:@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/lib\n" +
        "MPI_ARCH_PATH=@KIT@/ThirdParty-v2606/platforms/linux64Gcc/openmpi-4.1.8\n" +
        "PATH=@KIT@/ThirdParty-v2606/platforms/linux64Gcc/openmpi-4.1.8/bin:@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/bin:@KIT@/OpenFOAM-v2606/bin\n" +
        "WM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n" +
        "HOME=@SCRATCH@/home\n" +
        "TMPDIR=@SCRATCH@/tmp\n" +
        "WM_PROJECT_USER_DIR=@SCRATCH@/home/OpenFOAM/user-v2606\n" +
        "FOAM_SIGFPE=true\n" +
        "KIT_EXECUTABLE_DIRS=@KIT@/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/bin:@KIT@/OpenFOAM-v2606/bin:@KIT@/ThirdParty-v2606/platforms/linux64Gcc/openmpi-4.1.8/bin\n" +
        "KIT_PLATFORM=linux-x64\n" +
        "KIT_MPI=openmpi-4.1.8\n";

    #region Parse Tests

    [Test]
    public void ParseKeepsEveryAssignmentInOrderAndSkipsCommentsTest()
    {
        var environment = FoamKitEnvironment.Parse(LINUX_KIT_ENV);

        Assert.That(environment.Entries.Select(entry => entry.Key).Take(3), Is.EqualTo(new[] { "FOAM_APPBIN", "FOAM_LIBBIN", "FOAM_MPI" }));
        Assert.That(environment.Get("FOAM_MPI"), Is.EqualTo("openmpi-4.1.8"));
        Assert.That(environment.Get(FoamKitEnvironment.PLATFORM), Is.EqualTo("linux-x64"));
        Assert.That(environment.Get("NOT_THERE"), Is.Null);
    }

    [Test]
    public void ParseRefusesAFileWithoutTheProjectDirectoryOrPathTest()
    {
        Assert.That(() => FoamKitEnvironment.Parse("PATH=@KIT@/bin\n"), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => FoamKitEnvironment.Parse("WM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n"), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ALaterAssignmentReplacesAnEarlierOneTest()
    {
        var environment = FoamKitEnvironment.Parse("WM_PROJECT_DIR=@KIT@/a\nPATH=@KIT@/bin\nWM_PROJECT_DIR=@KIT@/b\n");

        Assert.That(environment.Get("WM_PROJECT_DIR"), Is.EqualTo("@KIT@/b"));
        Assert.That(environment.Entries.Count(entry => entry.Key == "WM_PROJECT_DIR"), Is.EqualTo(1));
    }

    #endregion

    #region Resolve Tests

    [Test]
    public void ResolveSubstitutesBothPlaceholdersAndAppendsTheSystemPathTest()
    {
        var environment = FoamKitEnvironment.Parse(LINUX_KIT_ENV);

        var resolved = environment.Resolve("/opt/kits/openfoam/linux-x64/", "/tmp/outwit-foam/abc");

        Assert.That(resolved["WM_PROJECT_DIR"], Is.EqualTo("/opt/kits/openfoam/linux-x64/OpenFOAM-v2606"));
        Assert.That(resolved["HOME"], Is.EqualTo("/tmp/outwit-foam/abc/home"));
        Assert.That(resolved["TMPDIR"], Is.EqualTo("/tmp/outwit-foam/abc/tmp"));
        Assert.That(resolved["PATH"], Does.StartWith("/opt/kits/openfoam/linux-x64/ThirdParty-v2606/platforms/linux64Gcc/openmpi-4.1.8/bin"));
        Assert.That(resolved["PATH"], Does.EndWith(FoamKitEnvironment.SystemPath()));
        Assert.That(resolved.Keys, Does.Not.Contain("NOT_THERE"));

        foreach (var (_, value) in resolved)
        {
            Assert.That(value, Does.Not.Contain(FoamKitEnvironment.KIT_TOKEN));
            Assert.That(value, Does.Not.Contain(FoamKitEnvironment.SCRATCH_TOKEN));
        }
    }

    [Test]
    public void ResolveWritesForwardSlashesWhateverTheHostUsesTest()
    {
        var environment = FoamKitEnvironment.Parse(LINUX_KIT_ENV);

        var resolved = environment.Resolve(@"C:\kits\openfoam\windows-x64", @"C:\Temp\outwit-foam\abc");

        Assert.That(resolved["WM_PROJECT_DIR"], Is.EqualTo("C:/kits/openfoam/windows-x64/OpenFOAM-v2606"));
        Assert.That(resolved["HOME"], Is.EqualTo("C:/Temp/outwit-foam/abc/home"));
    }

    [Test]
    public void ExecutableDirectoriesAndPathEntriesAreSplitAndSubstitutedTest()
    {
        var environment = FoamKitEnvironment.Parse(LINUX_KIT_ENV);

        var directories = environment.ExecutableDirectories("/kit");
        var pathEntries = environment.PathEntries("/kit");

        Assert.That(directories, Has.Count.EqualTo(3));
        Assert.That(directories[0], Is.EqualTo("/kit/OpenFOAM-v2606/platforms/linux64GccDPInt32Opt/bin"));
        Assert.That(pathEntries, Has.Count.EqualTo(3));
        Assert.That(pathEntries[0], Is.EqualTo("/kit/ThirdParty-v2606/platforms/linux64Gcc/openmpi-4.1.8/bin"));
    }

    [Test]
    public void AWindowsKitEnvHasNoExecutableDirectoriesAndASinglePathEntryTest()
    {
        var environment = FoamKitEnvironment.Parse(
            "PATH=@KIT@/OpenFOAM-v2606/platforms/win64MingwDPInt32Opt/bin\n" +
            "WM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n" +
            "KIT_PLATFORM=windows-x64\n");

        Assert.That(environment.ExecutableDirectories("C:/kit"), Is.Empty);
        Assert.That(environment.PathEntries("C:/kit"), Is.EqualTo(new[] { "C:/kit/OpenFOAM-v2606/platforms/win64MingwDPInt32Opt/bin" }));
    }

    #endregion
}
