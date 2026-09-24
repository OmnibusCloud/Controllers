using System.Runtime.InteropServices;

namespace OutWit.Controller.OpenFOAM.Tests.Utils;

internal static class OpenFOAMTestPaths
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

    /// <summary>
    /// Locates the fake-foam apphost built by the test-only FakeFoam project,
    /// preferring the configuration this test assembly was built in.
    /// </summary>
    /// <param name="solutionRoot">Repository root.</param>
    /// <returns>Full path of the fake solver, or null when it is not built.</returns>
    public static string? FindFakeFoamPath(string solutionRoot)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fake-foam.exe" : "fake-foam";
        var projectDir = Path.Combine(solutionRoot, "OpenFOAM", "OutWit.Controller.OpenFOAM.Tests.FakeFoam", "bin");

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

    /// <summary>
    /// A fresh temporary directory without a space in its path (OpenFOAM's
    /// rule), removed by the caller.
    /// </summary>
    /// <param name="prefix">Name prefix.</param>
    /// <returns>The created directory.</returns>
    public static string CreateScratch(string prefix)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    #endregion
}
