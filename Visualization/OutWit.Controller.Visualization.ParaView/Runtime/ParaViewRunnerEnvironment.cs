using System.Runtime.InteropServices;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// Builds the pvpython child's environment from an allowlist (docs 03, section 14): nothing of
/// the worker's environment leaks through except the few variables the process needs to start;
/// HOME/APPDATA/TEMP point inside the task's work directory so no user configuration, plugin
/// path or cache outside it is ever consulted or written; DISPLAY is never passed; and the
/// software-rendering baseline is selected through VTK's backend variable on Linux.
/// </summary>
public static class ParaViewRunnerEnvironment
{
    #region Constants

    /// <summary>VTK's render-window backend selector; OSMesa is the certified software baseline.</summary>
    public const string VTK_DEFAULT_OPENGL_WINDOW = "VTK_DEFAULT_OPENGL_WINDOW";

    /// <summary>The OSMesa render window class.</summary>
    public const string OSMESA_WINDOW = "vtkOSOpenGLRenderWindow";

    private static readonly string[] WINDOWS_PASSTHROUGH =
    [
        "SystemRoot",
        "SystemDrive",
        "windir",
        "ComSpec",
        "NUMBER_OF_PROCESSORS",
        "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_IDENTIFIER",
        "ProgramData",
        "CommonProgramFiles",
        "ProgramFiles",
        "ProgramFiles(x86)",
        "PATHEXT"
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Builds the allowlisted environment of one task run.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython path (its directory joins PATH).</param>
    /// <param name="homeDirectory">Task-private home directory.</param>
    /// <param name="tempDirectory">Task-private temp directory.</param>
    /// <param name="forceSoftwareRendering">Select the OSMesa backend through VTK's variable (Linux).</param>
    /// <returns>Variable name → value.</returns>
    public static IReadOnlyDictionary<string, string> Build(
        string pvpythonPath,
        string homeDirectory,
        string tempDirectory,
        bool forceSoftwareRendering)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var binDirectory = Path.GetDirectoryName(pvpythonPath) ?? string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var name in WINDOWS_PASSTHROUGH)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value))
                    environment[name] = value;
            }

            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            environment["PATH"] = string.Join(Path.PathSeparator, binDirectory, Path.Combine(systemRoot, "System32"), systemRoot);
            environment["USERPROFILE"] = homeDirectory;
            environment["HOMEDRIVE"] = Path.GetPathRoot(homeDirectory)?.TrimEnd('\\') ?? string.Empty;
            environment["HOMEPATH"] = homeDirectory.Length > 2 ? homeDirectory[2..] : homeDirectory;
            environment["APPDATA"] = homeDirectory;
            environment["LOCALAPPDATA"] = homeDirectory;
            environment["TEMP"] = tempDirectory;
            environment["TMP"] = tempDirectory;
        }
        else
        {
            environment["PATH"] = string.Join(Path.PathSeparator, binDirectory, "/usr/bin", "/bin");
            environment["HOME"] = homeDirectory;
            environment["TMPDIR"] = tempDirectory;
            environment["LANG"] = "C.UTF-8";
            environment["LC_ALL"] = "C.UTF-8";
            environment["XDG_CONFIG_HOME"] = Path.Combine(homeDirectory, ".config");
            environment["XDG_CACHE_HOME"] = Path.Combine(homeDirectory, ".cache");
            environment["XDG_DATA_HOME"] = Path.Combine(homeDirectory, ".local", "share");
        }

        environment["PYTHONDONTWRITEBYTECODE"] = "1";
        environment["PYTHONNOUSERSITE"] = "1";
        environment["PYTHONIOENCODING"] = "utf-8";
        environment["PYTHONUTF8"] = "1";

        if (forceSoftwareRendering)
            environment[VTK_DEFAULT_OPENGL_WINDOW] = OSMESA_WINDOW;

        return environment;
    }

    /// <summary>
    /// The software-rendering baseline applies on Linux (OSMesa is the certified path). Windows and
    /// macOS run pvpython's offscreen path on the platform's own OpenGL window until their runtime
    /// assets are certified by the platform-completion milestone.
    /// </summary>
    /// <returns>True when VTK_DEFAULT_OPENGL_WINDOW should select OSMesa.</returns>
    public static bool ForceSoftwareRenderingByDefault()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    #endregion
}
