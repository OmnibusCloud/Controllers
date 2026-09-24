using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Tests.Utils;

/// <summary>
/// A kit folder shaped like the real one - KIT.env, FOAM_APPBIN, the
/// tutorials directory - whose every "executable" is a copy of fake-foam.
/// Enough for the runner, the session and the benchmark to go through their
/// motions without OpenFOAM.
/// </summary>
internal sealed class FakeKit : IDisposable
{
    #region Constants

    private const string PLATFORM_FOLDER = "fake";

    private const string PROJECT = "OpenFOAM-v2606";

    #endregion

    #region Constructors

    private FakeKit(string root, string fakeFoamPath)
    {
        Root = root;
        FakeFoamPath = fakeFoamPath;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a kit whose named utilities are all the fake solver.
    /// </summary>
    /// <param name="fakeFoamPath">Path of the built fake-foam.</param>
    /// <param name="utilities">Executable names to provide.</param>
    /// <returns>The kit; dispose to remove it.</returns>
    public static FakeKit Create(string fakeFoamPath, params string[] utilities)
    {
        var root = OpenFOAMTestPaths.CreateScratch("foam-kit");
        var project = Path.Combine(root, PROJECT);
        var appBin = Path.Combine(project, "platforms", PLATFORM_FOLDER, "bin");
        Directory.CreateDirectory(appBin);
        Directory.CreateDirectory(Path.Combine(project, "etc"));
        Directory.CreateDirectory(Path.Combine(project, "tutorials"));

        foreach (var utility in utilities.Append("simpleFoam").Distinct())
            File.Copy(fakeFoamPath, Path.Combine(appBin, utility + ExecutableExtension), overwrite: true);

        // The libraries the response function objects name: the inspector
        // checks every libs entry against the kit.
        var libBin = Path.Combine(project, "platforms", PLATFORM_FOLDER, "lib");
        Directory.CreateDirectory(libBin);
        foreach (var library in new[] { "libforces.so", "libfieldFunctionObjects.so", "libsampling.so" })
            File.WriteAllText(Path.Combine(libBin, library), string.Empty);

        // The benchmark's reference case, in the shape the real kit ships it:
        // controlDict with an endTime the benchmark rewrites, an fvSolution
        // with residualControl it removes, initial fields under 0.orig.
        var pitzDaily = Path.Combine(project, "tutorials", "incompressible", "simpleFoam", "pitzDaily");
        Directory.CreateDirectory(Path.Combine(pitzDaily, "system"));
        Directory.CreateDirectory(Path.Combine(pitzDaily, "0.orig"));
        Directory.CreateDirectory(Path.Combine(pitzDaily, "constant"));
        File.WriteAllText(Path.Combine(pitzDaily, "system", "controlDict"), "application     simpleFoam;\nstartTime       0;\nendTime         2000;\nwriteInterval   100;\n");
        File.WriteAllText(Path.Combine(pitzDaily, "system", "fvSolution"), "SIMPLE\n{\n    residualControl\n    {\n        p               1e-2;\n        U               1e-3;\n    }\n}\n");
        File.WriteAllText(Path.Combine(pitzDaily, "system", "blockMeshDict"), "vertices ();\n");
        File.WriteAllText(Path.Combine(pitzDaily, "0.orig", "U"), "internalField uniform (0 0 0);\n");
        File.WriteAllText(Path.Combine(pitzDaily, "constant", "transportProperties"), "nu 1e-05;\n");

        // The apphost carries the name of its managed assembly (fake-foam.dll)
        // and looks for it and the runtime config beside itself, whatever the
        // apphost file is called - so one copy of those serves every utility.
        var sourceDirectory = Path.GetDirectoryName(fakeFoamPath)!;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "fake-foam.*"))
        {
            if (!string.Equals(file, fakeFoamPath, StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(appBin, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(root, FoamKitEnvironment.FILE_NAME),
            "# fake kit\n" +
            $"PATH=@KIT@/{PROJECT}/platforms/{PLATFORM_FOLDER}/bin\n" +
            "WM_PROJECT=OpenFOAM\n" +
            "WM_PROJECT_VERSION=v2606\n" +
            $"WM_PROJECT_DIR=@KIT@/{PROJECT}\n" +
            $"FOAM_APPBIN=@KIT@/{PROJECT}/platforms/{PLATFORM_FOLDER}/bin\n" +
            $"FOAM_LIBBIN=@KIT@/{PROJECT}/platforms/{PLATFORM_FOLDER}/lib\n" +
            $"FOAM_TUTORIALS=@KIT@/{PROJECT}/tutorials\n" +
            "HOME=@SCRATCH@/home\n" +
            "TMPDIR=@SCRATCH@/tmp\n" +
            "FOAM_SIGFPE=true\n" +
            $"KIT_EXECUTABLE_DIRS=@KIT@/{PROJECT}/platforms/{PLATFORM_FOLDER}/bin\n" +
            "KIT_PLATFORM=fake\n" +
            // The fake solver's own runtime need, declared where a real kit declares its library paths.
            $"DOTNET_ROOT={OpenFOAMTestPaths.DotnetRoot()}\n");

        return new FakeKit(root, fakeFoamPath);
    }

    /// <summary>
    /// Appends a line to the kit's KIT.env (a variable the default fake kit does not set).
    /// </summary>
    /// <param name="line">A NAME=VALUE line.</param>
    public void AppendEnvironment(string line)
    {
        File.AppendAllText(Path.Combine(Root, FoamKitEnvironment.FILE_NAME), line + "\n");
    }

    /// <summary>
    /// The path a kit-relative file has in this kit.
    /// </summary>
    /// <param name="relativePath">Path relative to the kit folder, with forward slashes.</param>
    /// <returns>The absolute path.</returns>
    public string PathOf(string relativePath)
    {
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Resolves the kit through the same code the adapter uses.
    /// </summary>
    /// <returns>The kit.</returns>
    public FoamKit Resolve()
    {
        return new FoamKit(Root, FoamKitEnvironment.Load(Path.Combine(Root, FoamKitEnvironment.FILE_NAME)));
    }

    /// <summary>
    /// Resolves the kit with an MPI launcher that is the fake solver itself:
    /// it prints the command line it was given (<c>-np N solver -parallel</c>)
    /// and then "solves" the case in its working directory, so the parallel
    /// path of the runner can be walked without MPI.
    /// </summary>
    /// <returns>The kit, with <see cref="FoamKit.SupportsParallel"/> true.</returns>
    public FoamKit ResolveWithLauncher()
    {
        var launcher = Path.Combine(AppBin, "mpirun" + ExecutableExtension);
        File.Copy(FakeFoamPath, launcher, overwrite: true);

        return new FoamKit(Root, FoamKitEnvironment.Load(Path.Combine(Root, FoamKitEnvironment.FILE_NAME)), launcher);
    }

    public void Dispose()
    {
        OpenFOAMTestPaths.TryDelete(Root);
    }

    #endregion

    #region Properties

    public string Root { get; }

    public string FakeFoamPath { get; }

    /// <summary>The kit's FOAM_APPBIN, absolute.</summary>
    public string AppBin => Path.Combine(Root, PROJECT, "platforms", PLATFORM_FOLDER, "bin");

    /// <summary>The kit-relative FOAM_APPBIN, with forward slashes, as KIT.env spells it after @KIT@/.</summary>
    public static string AppBinRelative => $"{PROJECT}/platforms/{PLATFORM_FOLDER}/bin";

    private static string ExecutableExtension => OperatingSystem.IsWindows() ? ".exe" : string.Empty;

    #endregion
}
