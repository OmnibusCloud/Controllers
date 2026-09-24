using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Locates the bundled OpenFOAM kit inside the controller module. The asset
/// pipeline extracts each platform's archive at the module root, so the kit
/// sits at openfoam/&lt;runtime-folder&gt;/ next to the controller assembly.
/// Zip extraction keeps no Unix mode bits, so the resolver marks the files of
/// the directories KIT.env names executable; on Windows it puts the MS-MPI
/// Pstream in place when the node has MS-MPI (plan D-21), once.
/// </summary>
public static class FoamKitResolver
{
    #region Constants

    private const string KIT_DIRECTORY = "openfoam";

    /// <summary>
    /// Environment override of the kit folder - operator escape hatch and the
    /// test harness's seam. An explicit override wins even when the folder is
    /// wrong: a configured-but-wrong path must fail loudly, never fall through
    /// to a different kit silently.
    /// </summary>
    public const string ENV_KIT_PATH = "OUTWIT_OPENFOAM";

    private const string PSTREAM_TARGET = "KIT_PSTREAM_TARGET";

    private const string PSTREAM_MSMPI = "KIT_PSTREAM_MSMPI";

    private const string PROBE_EXECUTABLE = "simpleFoam";

    #endregion

    #region Fields

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> INTEGRITY = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Resolves the kit: the OUTWIT_OPENFOAM override first, then the bundled
    /// per-platform kit inside the module.
    /// </summary>
    /// <param name="controllerAssemblyPath">Path of the controller assembly, the module root anchor.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>The kit, or null when the module carries none for this platform.</returns>
    public static FoamKit? Resolve(string controllerAssemblyPath, ILogger? logger = null)
    {
        var root = ResolveRoot(controllerAssemblyPath, logger);
        if (root == null)
            return null;

        var envFile = Path.Combine(root, FoamKitEnvironment.FILE_NAME);
        if (!File.Exists(envFile))
        {
            logger?.LogWarning("Foam.Run: the kit at {Root} carries no KIT.env.", root);
            return null;
        }

        var environment = FoamKitEnvironment.Load(envFile);
        var kit = new FoamKit(root, environment);

        if (!kit.HasExecutable(PROBE_EXECUTABLE))
        {
            logger?.LogWarning("Foam.Run: the kit at {Root} has no {Probe} in {AppBin}.", root, PROBE_EXECUTABLE, kit.AppBin);
            return null;
        }

        if (!IsIntact(kit, logger))
            return null;

        EnsureExecutables(kit, logger);
        EnsurePstream(kit, logger);

        return kit;
    }

    /// <summary>
    /// The integrity spot check, once per kit folder per process: a kit that
    /// is short of a file or carries an altered one is refused here, with the
    /// file named, rather than failing in the middle of a case. A kit without
    /// a BUILDINFO (a test kit) is accepted with a warning.
    /// </summary>
    /// <param name="kit">The kit.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>True when the kit may be used.</returns>
    public static bool IsIntact(FoamKit kit, ILogger? logger = null)
    {
        return INTEGRITY.GetOrAdd(kit.Root, root =>
        {
            var findings = FoamKitIntegrity.Check(root);
            if (findings.Count == 0)
                return true;

            if (findings.Count == 1 && findings[0].Contains("carries no", StringComparison.Ordinal))
            {
                logger?.LogWarning("Foam.Run: {Finding} The kit at {Root} is used unchecked.", findings[0], root);
                return true;
            }

            foreach (var finding in findings)
                logger?.LogError("Foam.Run: the kit at {Root} is not intact - {Finding}", root, finding);

            return false;
        });
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

    /// <summary>
    /// Marks every file of the directories KIT.env names as executable (Linux,
    /// macOS). Idempotent: skipped when the probe executable already has the bit.
    /// </summary>
    /// <param name="kit">The kit.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>Number of files marked in this call.</returns>
    public static int EnsureExecutables(FoamKit kit, ILogger? logger = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return 0;

        var marked = 0;
        try
        {
            if (HasExecuteBit(kit.ExecutablePath(PROBE_EXECUTABLE)))
                return 0;

            foreach (var directory in kit.Environment.ExecutableDirectories(kit.Root))
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var mode = File.GetUnixFileMode(file);
                    var withExecute = mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                    if (withExecute == mode)
                        continue;

                    File.SetUnixFileMode(file, withExecute);
                    marked++;
                }
            }
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Foam.Run: failed to mark the kit's executables under {Root}.", kit.Root);
        }

        return marked;
    }

    /// <summary>
    /// Windows: copies the MS-MPI Pstream over the serial default when the
    /// node has MS-MPI, so parallel steps can run; a node without MS-MPI keeps
    /// the serial one and runs every step serially. Windows searches the
    /// executable's own directory before PATH, so this cannot be chosen per
    /// run through the environment - it is done once, here.
    /// </summary>
    /// <param name="kit">The kit.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <returns>True when the MS-MPI Pstream is in place after the call.</returns>
    public static bool EnsurePstream(FoamKit kit, ILogger? logger = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var target = kit.Environment.Get(PSTREAM_TARGET, kit.Root);
        var msmpi = kit.Environment.Get(PSTREAM_MSMPI, kit.Root);
        if (target == null || msmpi == null || !File.Exists(target) || !File.Exists(msmpi))
            return false;

        if (!kit.SupportsParallel)
            return false;

        try
        {
            if (SameContent(target, msmpi))
                return true;

            File.Copy(msmpi, target, overwrite: true);
            logger?.LogInformation("Foam.Run: MS-MPI found on this node - the MS-MPI Pstream is now the kit's libPstream.dll.");
            return true;
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Foam.Run: failed to put the MS-MPI Pstream in place; parallel steps stay unavailable.");
            return false;
        }
    }

    private static string? ResolveRoot(string controllerAssemblyPath, ILogger? logger)
    {
        var overridePath = Environment.GetEnvironmentVariable(ENV_KIT_PATH);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var runtimeFolder = ResolveCurrentRuntimeFolder();
        if (runtimeFolder == null)
        {
            logger?.LogWarning("Foam.Run: unsupported platform for the bundled OpenFOAM kit.");
            return null;
        }

        var moduleDirectory = Path.GetDirectoryName(controllerAssemblyPath);
        if (string.IsNullOrEmpty(moduleDirectory))
            return null;

        var root = Path.Combine(moduleDirectory, KIT_DIRECTORY, runtimeFolder);
        if (Directory.Exists(root))
            return root;

        logger?.LogWarning("Foam.Run: bundled OpenFOAM kit not found at {Root}.", root);
        return null;
    }

    private static bool HasExecuteBit(string path)
    {
        return File.Exists(path) && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    private static bool SameContent(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        using var firstStream = firstInfo.OpenRead();
        using var secondStream = secondInfo.OpenRead();
        return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
    }

    #endregion
}
