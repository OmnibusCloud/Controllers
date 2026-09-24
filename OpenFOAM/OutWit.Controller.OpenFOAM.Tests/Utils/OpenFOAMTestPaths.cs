using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Tests.Utils;

internal static class OpenFOAMTestPaths
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
    /// Locates the fake-foam apphost built by the test-only FakeFoam project,
    /// preferring the configuration this test assembly was built in.
    /// </summary>
    /// <param name="solutionRoot">Repository root.</param>
    /// <returns>Full path of the fake solver, or null when it is not built.</returns>
    public static string? FindFakeFoamPath(string solutionRoot)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fake-foam.exe" : "fake-foam";
        var projectDir = Path.Combine(solutionRoot, "OpenFOAM", "OutWit.Controller.OpenFOAM.Tests.FakeFoam", "bin");

        foreach (var configuration in Configurations())
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

    /// <summary>
    /// Writes a BUILDINFO.txt over a kit folder in the shape the pack step
    /// writes it: a header, then the SHA-256 of every file.
    /// </summary>
    /// <param name="kitRoot">The kit folder.</param>
    public static void WriteBuildInfo(string kitRoot)
    {
        var lines = new List<string> { "OpenFOAM v2606 (api 2606) - OmnibusCloud kit", "platform: test", "", "sha256 of every file (relative to the kit folder):" };
        foreach (var file in Directory.EnumerateFiles(kitRoot, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == FoamKitIntegrity.BUILDINFO)
                continue;

            var relative = Path.GetRelativePath(kitRoot, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            lines.Add($"{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}  {relative}");
        }

        File.WriteAllLines(Path.Combine(kitRoot, FoamKitIntegrity.BUILDINFO), lines);
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

    private static IEnumerable<string> Configurations()
    {
        var own = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
            ? "Release"
            : "Debug";

        return new[] { own, "Debug", "Release" }.Distinct();
    }

    #endregion
}
