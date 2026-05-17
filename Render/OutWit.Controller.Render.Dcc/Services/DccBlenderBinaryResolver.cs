using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlenderBinaryResolver
{
    #region Functions

    public static string ResolveBlenderPath(string controllerAssemblyPath, ILogger logger)
    {
        var blenderRoot = ResolveBlenderRoot(controllerAssemblyPath);
        var blenderPath = ResolveBlenderPathFromRoot(blenderRoot);
        EnsureExecutable(blenderPath, logger);
        return blenderPath;
    }

    public static string ResolveBlenderRoot(string controllerAssemblyPath)
    {
        var candidateRoots = new List<string>();

        var assemblyDirectory = Path.GetDirectoryName(controllerAssemblyPath);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            candidateRoots.Add(Path.Combine(assemblyDirectory, "blender"));
            candidateRoots.Add(Path.Combine(assemblyDirectory, "..", "render.module", "blender"));
        }

        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            candidateRoots.Add(Path.Combine(directory, "@Controllers", "Debug", "render.module", "blender"));
            candidateRoots.Add(Path.Combine(directory, "@Prerequisites", "blender"));
            directory = Path.GetDirectoryName(directory);
        }

        foreach (var candidateRoot in candidateRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(ResolveBlenderPathFromRoot(candidateRoot)))
                return candidateRoot;
        }

        return candidateRoots.First();
    }

    private static void EnsureExecutable(string blenderPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(blenderPath) || !File.Exists(blenderPath) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var currentMode = File.GetUnixFileMode(blenderPath);
            var requiredMode = currentMode
                               | UnixFileMode.UserExecute
                               | UnixFileMode.GroupExecute
                               | UnixFileMode.OtherExecute;

            if (currentMode == requiredMode)
                return;

            File.SetUnixFileMode(blenderPath, requiredMode);
            logger.LogInformation("Marked Blender executable as executable: {BlenderPath}", blenderPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark Blender executable as executable: {BlenderPath}", blenderPath);
        }
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

    private static string ResolveBlenderPathFromRoot(string blenderRoot)
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

        return ResolveBlenderPathInDirectory(candidateDirectories.First());
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

    #endregion
}
