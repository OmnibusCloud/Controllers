using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Locates the bundled ccx binary inside the controller module. The asset
/// pipeline extracts each platform's build under ccx/&lt;runtime-folder&gt;/
/// next to the controller assembly; zip extraction does not preserve the Unix
/// exec bit, so the resolver restores it before handing the path out.
/// </summary>
public static class CcxBinaryResolver
{
    #region Constants

    private const string TOOL_DIRECTORY = "ccx";

    /// <summary>
    /// Environment override of the solver path — operator escape hatch and the
    /// test harness's seam. An explicit override wins even when the file is
    /// missing: a configured-but-wrong path must fail loudly at spawn, never
    /// fall through to a different solver silently.
    /// </summary>
    public const string ENV_SOLVER_PATH = "OUTWIT_CCX";

    #endregion

    #region Functions

    /// <summary>
    /// Resolves the ccx executable: the OUTWIT_CCX environment override first,
    /// then the bundled per-platform build inside the module.
    /// </summary>
    /// <param name="controllerAssemblyPath">Path of the controller assembly, the module root anchor.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>Full path of an executable ccx, or null when the module carries none for this platform.</returns>
    public static string? Resolve(string controllerAssemblyPath, ILogger? logger = null)
    {
        var overridePath = Environment.GetEnvironmentVariable(ENV_SOLVER_PATH);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var runtimeFolder = ResolveCurrentRuntimeFolder();
        if (runtimeFolder == null)
        {
            logger?.LogWarning("Ccx.Solve: unsupported platform for the bundled ccx.");
            return null;
        }

        var moduleDirectory = Path.GetDirectoryName(controllerAssemblyPath);
        if (string.IsNullOrEmpty(moduleDirectory))
            return null;

        var toolRoot = Path.Combine(moduleDirectory, TOOL_DIRECTORY, runtimeFolder);
        if (!Directory.Exists(toolRoot))
        {
            logger?.LogWarning("Ccx.Solve: bundled ccx not found at {ToolRoot}.", toolRoot);
            return null;
        }

        var executable = FindExecutable(toolRoot);
        if (executable == null)
        {
            logger?.LogWarning("Ccx.Solve: no ccx executable inside {ToolRoot}.", toolRoot);
            return null;
        }

        EnsureExecutable(executable, logger);
        return executable;
    }

    /// <summary>
    /// Maps the current OS/architecture to the asset extraction folder name.
    /// Deliberately the extract-folder vocabulary (windows-x64/macos-arm64),
    /// which differs from RIDs (win-x64/osx-arm64).
    /// </summary>
    /// <returns>The runtime folder name, or null on an unsupported platform.</returns>
    public static string? ResolveCurrentRuntimeFolder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "windows-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "linux-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return "macos-arm64";

        return null;
    }

    private static string? FindExecutable(string toolRoot)
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ccx.exe" : "ccx";

        // The pinned archives may nest the binary (bin/, versioned dirs) —
        // probe the root first, then search.
        var direct = Path.Combine(toolRoot, expected);
        if (File.Exists(direct))
            return direct;

        return Directory
            .EnumerateFiles(toolRoot, expected, SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? Directory
                .EnumerateFiles(toolRoot, "ccx*", SearchOption.AllDirectories)
                .FirstOrDefault(path => !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                        && !path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                                        && !path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureExecutable(string path, ILogger? logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            var withExecute = mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

            if (withExecute != mode)
                File.SetUnixFileMode(path, withExecute);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Ccx.Solve: failed to set the execute bit on {Path}.", path);
        }
    }

    #endregion
}
