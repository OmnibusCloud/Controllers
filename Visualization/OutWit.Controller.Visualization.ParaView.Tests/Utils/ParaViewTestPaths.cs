using System.Runtime.InteropServices;

namespace OutWit.Controller.Visualization.ParaView.Tests.Utils;

internal static class ParaViewTestPaths
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

    public static string? FindBundledScriptsPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Scripts", "Debug");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>The controller's author-side RuntimeTools directory (repository checkouts only).</summary>
    public static string? FindRuntimeToolsPath()
    {
        var root = FindSolutionRoot();
        if (root == null)
            return null;

        var tools = Path.Combine(root, "Visualization", "OutWit.Controller.Visualization.ParaView", "RuntimeTools");
        return Directory.Exists(tools) ? tools : null;
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

    public static string? FindFakePvpythonPath(string solutionRoot)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fake-pvpython.exe" : "fake-pvpython";
        var projectDir = Path.Combine(solutionRoot, "Visualization", "OutWit.Controller.Visualization.ParaView.Tests.FakePvpython", "bin");

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

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }

    #endregion
}
