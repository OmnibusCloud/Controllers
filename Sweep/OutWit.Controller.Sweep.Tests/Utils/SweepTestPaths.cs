using System.Runtime.InteropServices;

namespace OutWit.Controller.Sweep.Tests.Utils;

internal static class SweepTestPaths
{
    #region Functions

    /// <summary>
    /// Locates the staged controller modules (@Controllers/&lt;configuration&gt;),
    /// preferring the configuration this test assembly was built in.
    /// </summary>
    /// <returns>The module folder, or null when nothing is staged.</returns>
    public static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            foreach (var configuration in Configurations())
            {
                var candidate = Path.Combine(dir, "@Controllers", configuration);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    public static string? FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// The bundled script of a family, as the Scripts package ships it.
    /// </summary>
    /// <param name="solutionRoot">Repository root.</param>
    /// <param name="scriptName">The script's name without extension (<c>SweepCalculiX</c>, <c>SweepOpenFOAM</c>).</param>
    /// <returns>The script's path.</returns>
    public static string GetScriptPath(string solutionRoot, string scriptName)
    {
        return Path.Combine(solutionRoot, "Sweep", "OutWit.Controller.Sweep", "Scripts", $"{scriptName}.wit");
    }

    /// <summary>
    /// Locates the fake-ccx apphost built by the test-only FakeCcx project,
    /// preferring the configuration this test assembly was built in.
    /// </summary>
    /// <param name="solutionRoot">Repository root.</param>
    /// <returns>Full path of the fake solver, or null when it is not built.</returns>
    public static string? FindFakeCcxPath(string solutionRoot)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fake-ccx.exe" : "fake-ccx";
        var projectDir = Path.Combine(solutionRoot, "CalculiX", "OutWit.Controller.CalculiX.Tests.FakeCcx", "bin");

        foreach (var configuration in Configurations())
        {
            var candidate = Path.Combine(projectDir, configuration, "net10.0", exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> Configurations()
    {
        var own = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
            ? "Release"
            : "Debug";

        return new[] { own, "Debug", "Release" }.Distinct();
    }

    #endregion
}
