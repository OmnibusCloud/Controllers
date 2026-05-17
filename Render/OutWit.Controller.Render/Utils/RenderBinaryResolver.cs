using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Resolves runtime tool paths inside the render controller package.
/// Supports both a direct tool directory and a parent directory containing RID-specific subdirectories.
/// </summary>
internal static class RenderBinaryResolver
{
    #region Functions

    public static string ResolveBlenderRoot(string controllerAssemblyPath)
    {
        return ResolveToolRoot(controllerAssemblyPath, "blender", ResolveBlenderPath);
    }

    public static string ResolveBlenderPath(string blenderRoot)
    {
        var candidateDirectories = new List<string>();
        var runtimeTarget = ResolveCurrentRuntimeTarget();
        if (runtimeTarget != null)
            candidateDirectories.Add(Path.Combine(blenderRoot, runtimeTarget));

        candidateDirectories.Add(blenderRoot);

        foreach (var candidateDirectory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidatePath = ResolveBlenderPathInDirectory(candidateDirectory);
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        var fallbackDirectory = candidateDirectories.First();
        return ResolveBlenderPathInDirectory(fallbackDirectory);
    }

    public static string ResolveFfmpegRoot(string controllerAssemblyPath)
    {
        return ResolveToolRoot(controllerAssemblyPath, "ffmpeg", ResolveFfmpegPath);
    }

    public static string ResolveFfprobePath(string ffmpegRoot)
    {
        var candidateDirectories = new List<string>();
        var runtimeTarget = ResolveCurrentRuntimeTarget();
        if (runtimeTarget != null)
            candidateDirectories.Add(Path.Combine(ffmpegRoot, runtimeTarget));

        candidateDirectories.Add(ffmpegRoot);

        foreach (var candidateDirectory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidatePath = ResolveFfprobePathInDirectory(candidateDirectory);
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        var fallbackDirectory = candidateDirectories.First();
        return ResolveFfprobePathInDirectory(fallbackDirectory);
    }

    public static string ResolveFfmpegPath(string ffmpegRoot)
    {
        var candidateDirectories = new List<string>();
        var runtimeTarget = ResolveCurrentRuntimeTarget();
        if (runtimeTarget != null)
            candidateDirectories.Add(Path.Combine(ffmpegRoot, runtimeTarget));

        candidateDirectories.Add(ffmpegRoot);

        foreach (var candidateDirectory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidatePath = ResolveFfmpegPathInDirectory(candidateDirectory);
            if (File.Exists(candidatePath))
                return candidatePath;
        }

        var fallbackDirectory = candidateDirectories.First();
        return ResolveFfmpegPathInDirectory(fallbackDirectory);
    }

    public static string? GetCurrentRuntimeTarget()
    {
        return ResolveCurrentRuntimeTarget();
    }

    public static void EnsureExecutable(string path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var currentMode = File.GetUnixFileMode(path);
            var requiredMode = currentMode
                               | UnixFileMode.UserExecute
                               | UnixFileMode.GroupExecute
                               | UnixFileMode.OtherExecute;

            if (currentMode == requiredMode)
                return;

            File.SetUnixFileMode(path, requiredMode);
            logger.LogInformation("Marked runtime binary as executable: {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark runtime binary as executable: {Path}", path);
        }
    }

    #endregion

    #region Tools

    private static string ResolveToolRoot(
        string controllerAssemblyPath,
        string toolDirectoryName,
        Func<string, string> resolveBinaryPath)
    {
        var candidateRoots = new List<string>();

        var assemblyDirectory = Path.GetDirectoryName(controllerAssemblyPath);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
            candidateRoots.Add(Path.Combine(assemblyDirectory, toolDirectoryName));

        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            candidateRoots.Add(Path.Combine(dir, "@Controllers", "Debug", "render.module", toolDirectoryName));
            candidateRoots.Add(Path.Combine(dir, "@Prerequisites", toolDirectoryName));
            dir = Path.GetDirectoryName(dir);
        }

        foreach (var candidateRoot in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(resolveBinaryPath(candidateRoot)))
                return candidateRoot;
        }

        return candidateRoots.First();
    }

    private static string? ResolveCurrentRuntimeTarget()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return "windows-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return "linux-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return "macos-arm64";

        return null;
    }

    private static string ResolveBlenderPathInDirectory(string blenderDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(blenderDirectory, "blender.exe");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var appBundlePath = Path.Combine(blenderDirectory, "Blender.app", "Contents", "MacOS", "Blender");
            if (File.Exists(appBundlePath))
                return appBundlePath;

            return Path.Combine(blenderDirectory, "blender");
        }

        return Path.Combine(blenderDirectory, "blender");
    }

    private static string ResolveFfmpegPathInDirectory(string ffmpegDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(ffmpegDirectory, "ffmpeg.exe");

        return Path.Combine(ffmpegDirectory, "ffmpeg");
    }

    private static string ResolveFfprobePathInDirectory(string ffmpegDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(ffmpegDirectory, "ffprobe.exe");

        return Path.Combine(ffmpegDirectory, "ffprobe");
    }

    #endregion
}
