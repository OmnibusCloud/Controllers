using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Locates the bundled pvpython inside the controller module. The asset pipeline extracts each
/// platform's runtime under paraview/&lt;runtime-folder&gt;/ next to the controller assembly (the
/// extract-folder vocabulary windows-x64 / linux-x64 / macos-arm64, which differs from the RIDs);
/// zip extraction does not preserve the Unix exec bit, so the resolver restores it before handing
/// the path out. An explicit OUTWIT_PVPYTHON override wins even when the file is missing: a
/// configured-but-wrong path must fail loudly at spawn, never fall through to another runtime.
/// </summary>
public static class ParaViewBinaryResolver
{
    #region Constants

    /// <summary>
    /// Binaries the official Linux/macOS pvpython launcher exec's (bin/pvpython is a small ELF that
    /// sets LD_LIBRARY_PATH and the Mesa fallback, then runs pvpython-real); the zip archive drops
    /// their execute bits too.
    /// </summary>
    private static readonly string[] LAUNCHER_TARGETS = ["pvpython-real", "pvbatch", "pvbatch-real"];

    /// <summary>Module subdirectory holding the per-platform runtimes.</summary>
    public const string TOOL_DIRECTORY = "paraview";

    /// <summary>
    /// Environment override of the pvpython path — operator escape hatch and the test harness's seam.
    /// </summary>
    public const string ENV_PVPYTHON_PATH = "OUTWIT_PVPYTHON";

    #endregion

    #region Functions

    /// <summary>
    /// Resolves the pvpython executable: the OUTWIT_PVPYTHON override first, then the bundled
    /// per-platform runtime inside the module.
    /// </summary>
    /// <param name="controllerAssemblyPath">Path of the controller assembly, the module root anchor.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>Full path of an executable pvpython, or null when the module carries none for this platform.</returns>
    public static string? Resolve(string controllerAssemblyPath, ILogger? logger = null)
    {
        var overridePath = Environment.GetEnvironmentVariable(ENV_PVPYTHON_PATH);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var runtimeFolder = ResolveCurrentRuntimeFolder();
        if (runtimeFolder == null)
        {
            logger?.LogWarning("ParaView.RenderFrame: unsupported platform for the bundled ParaView runtime.");
            return null;
        }

        var toolRoot = ResolveToolRoot(controllerAssemblyPath, runtimeFolder);
        if (toolRoot == null)
        {
            logger?.LogWarning("ParaView.RenderFrame: bundled ParaView runtime not found for {RuntimeFolder}.", runtimeFolder);
            return null;
        }

        var executable = FindExecutable(toolRoot);
        if (executable == null)
        {
            logger?.LogWarning("ParaView.RenderFrame: no pvpython inside {ToolRoot}.", toolRoot);
            return null;
        }

        EnsureExecutable(executable, logger);
        foreach (var sibling in LAUNCHER_TARGETS)
            EnsureExecutable(Path.Combine(Path.GetDirectoryName(executable)!, sibling), logger);

        return executable;
    }

    /// <summary>
    /// Maps the current OS/architecture to the asset extraction folder name.
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

    /// <summary>
    /// Finds pvpython under a runtime root: bin/pvpython(.exe) first, then anywhere below (the
    /// archives may nest the tree one level, and macOS ships an .app bundle).
    /// </summary>
    /// <param name="toolRoot">The extracted runtime root.</param>
    /// <returns>The executable path or null.</returns>
    public static string? FindExecutable(string toolRoot)
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pvpython.exe" : "pvpython";

        var direct = Path.Combine(toolRoot, "bin", expected);
        if (File.Exists(direct))
            return direct;

        direct = Path.Combine(toolRoot, expected);
        if (File.Exists(direct))
            return direct;

        if (!Directory.Exists(toolRoot))
            return null;

        return Directory
            .EnumerateFiles(toolRoot, expected, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Restores the Unix execute bit on a runtime binary (zip extraction drops it). No-op on Windows.
    /// </summary>
    /// <param name="path">Binary path.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public static void EnsureExecutable(string path, ILogger? logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !File.Exists(path))
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
            logger?.LogWarning(e, "ParaView.RenderFrame: failed to set the execute bit on {Path}.", path);
        }
    }

    #endregion

    #region Tools

    private static string? ResolveToolRoot(string controllerAssemblyPath, string runtimeFolder)
    {
        var candidates = new List<string>();

        var moduleDirectory = Path.GetDirectoryName(controllerAssemblyPath);
        if (!string.IsNullOrEmpty(moduleDirectory))
            candidates.Add(Path.Combine(moduleDirectory, TOOL_DIRECTORY, runtimeFolder));

        // Dev-time fallbacks: the staged module at the solution root and the author-side
        // prerequisites tree, mirroring the Render controller's resolver.
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            candidates.Add(Path.Combine(dir, "@Controllers", "Debug", "paraview.module", TOOL_DIRECTORY, runtimeFolder));
            candidates.Add(Path.Combine(dir, "@Prerequisites", TOOL_DIRECTORY, runtimeFolder));
            dir = Path.GetDirectoryName(dir);
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Directory.Exists);
    }

    #endregion
}
