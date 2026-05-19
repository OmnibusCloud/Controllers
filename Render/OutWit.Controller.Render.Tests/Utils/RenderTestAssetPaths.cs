using System.Runtime.InteropServices;

namespace OutWit.Controller.Render.Tests.Utils;

internal static class RenderTestAssetPaths
{
    #region Constants

    private const string BLENDER_ROOT_SUBPATH = "@Prerequisites/blender";
    private const string BENCHMARK_ROOT_SUBPATH = "@Prerequisites/benchmark/render";
    private const string TEST_SCENE_SUBPATH = "@Prerequisites/test_scene.blend";
    private const string CUBE_DIORAMA_SCENE_SUBPATH = "@Data/cube_diorama/cube_diorama.blend";
    private const string BENCHMARK_SCENE_SUBPATH = "@Prerequisites/benchmark/render/benchmark_scene.blend";
    private const string BENCHMARK_STILL_SCENE_SUBPATH = "@Prerequisites/benchmark/render/benchmark_scene_still.blend";
    private const string BENCHMARK_VIDEO_SCENE_SUBPATH = "@Prerequisites/benchmark/render/benchmark_scene_video.blend";

    #endregion

    #region Functions

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

    public static string? ResolveBlenderDir(string solutionRoot)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return Path.Combine(solutionRoot, BLENDER_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return Path.Combine(solutionRoot, BLENDER_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return Path.Combine(solutionRoot, BLENDER_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));

        return null;
    }

    public static string GetBenchmarkScenePath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, BENCHMARK_SCENE_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetBenchmarkStillScenePath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, BENCHMARK_STILL_SCENE_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetBenchmarkVideoScenePath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, BENCHMARK_VIDEO_SCENE_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetBenchmarkRootPath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, BENCHMARK_ROOT_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetTestScenePath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, TEST_SCENE_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string GetCubeDioramaScenePath(string solutionRoot)
    {
        return Path.Combine(solutionRoot, CUBE_DIORAMA_SCENE_SUBPATH.Replace('/', Path.DirectorySeparatorChar));
    }

    #endregion
}
