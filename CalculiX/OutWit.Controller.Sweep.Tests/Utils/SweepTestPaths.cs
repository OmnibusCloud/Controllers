using System.Runtime.InteropServices;

namespace OutWit.Controller.Sweep.Tests.Utils;

internal static class SweepTestPaths
{
    #region Functions

    public static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;

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

    public static string GetSweepSolveScriptPath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, "CalculiX", "OutWit.Controller.Sweep", "Scripts", "SweepSolve.wit");
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

        var ownConfiguration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
            ? "Release"
            : "Debug";

        foreach (var configuration in new[] { ownConfiguration, "Debug", "Release" })
        {
            var candidate = Path.Combine(projectDir, configuration, "net10.0", exeName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    #endregion
}
