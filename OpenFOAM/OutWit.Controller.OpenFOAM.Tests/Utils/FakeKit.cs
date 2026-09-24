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

    #endregion

    #region Constructors

    private FakeKit(string root)
    {
        Root = root;
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
        var project = Path.Combine(root, "OpenFOAM-v2606");
        var appBin = Path.Combine(project, "platforms", PLATFORM_FOLDER, "bin");
        Directory.CreateDirectory(appBin);
        Directory.CreateDirectory(Path.Combine(project, "etc"));
        Directory.CreateDirectory(Path.Combine(project, "tutorials"));

        var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var utility in utilities.Append("simpleFoam").Distinct())
            File.Copy(fakeFoamPath, Path.Combine(appBin, utility + extension), overwrite: true);

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
            $"PATH=@KIT@/OpenFOAM-v2606/platforms/{PLATFORM_FOLDER}/bin\n" +
            "WM_PROJECT=OpenFOAM\n" +
            "WM_PROJECT_VERSION=v2606\n" +
            "WM_PROJECT_DIR=@KIT@/OpenFOAM-v2606\n" +
            $"FOAM_APPBIN=@KIT@/OpenFOAM-v2606/platforms/{PLATFORM_FOLDER}/bin\n" +
            $"FOAM_LIBBIN=@KIT@/OpenFOAM-v2606/platforms/{PLATFORM_FOLDER}/lib\n" +
            "FOAM_TUTORIALS=@KIT@/OpenFOAM-v2606/tutorials\n" +
            "HOME=@SCRATCH@/home\n" +
            "TMPDIR=@SCRATCH@/tmp\n" +
            "FOAM_SIGFPE=true\n" +
            $"KIT_EXECUTABLE_DIRS=@KIT@/OpenFOAM-v2606/platforms/{PLATFORM_FOLDER}/bin\n" +
            "KIT_PLATFORM=fake\n");

        return new FakeKit(root);
    }

    /// <summary>
    /// Resolves the kit through the same code the adapter uses.
    /// </summary>
    /// <returns>The kit.</returns>
    public FoamKit Resolve()
    {
        return new FoamKit(Root, FoamKitEnvironment.Load(Path.Combine(Root, FoamKitEnvironment.FILE_NAME)));
    }

    public void Dispose()
    {
        OpenFOAMTestPaths.TryDelete(Root);
    }

    #endregion

    #region Properties

    public string Root { get; }

    #endregion
}
