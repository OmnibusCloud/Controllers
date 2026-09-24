namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// A resolved OpenFOAM kit on this node: its folder, its environment, where
/// its executables are and how a parallel step is launched. Built once by
/// <see cref="FoamKitResolver"/>; everything a run needs about the kit is
/// answered here without touching KIT.env again.
/// </summary>
public sealed class FoamKit
{
    #region Constants

    private const string MPIRUN = "mpirun";

    private const string MPIEXEC = "mpiexec.exe";

    /// <summary>The Windows variable Microsoft's MS-MPI installer sets system-wide.</summary>
    public const string MSMPI_BIN = "MSMPI_BIN";

    #endregion

    #region Constructors

    /// <summary>
    /// Describes a kit.
    /// </summary>
    /// <param name="root">The kit folder (the one holding KIT.env).</param>
    /// <param name="environment">Its parsed KIT.env.</param>
    /// <exception cref="InvalidDataException">KIT.env names no FOAM_APPBIN.</exception>
    public FoamKit(string root, FoamKitEnvironment environment)
    {
        Root = Path.GetFullPath(root);
        Environment = environment;
        Platform = environment.Get(FoamKitEnvironment.PLATFORM) ?? string.Empty;
        AppBin = environment.Get("FOAM_APPBIN", Root) ?? throw new InvalidDataException("KIT.env names no FOAM_APPBIN.");
        MpiLauncher = FindMpiLauncher(environment, Root);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The full path of an executable of the kit.
    /// </summary>
    /// <param name="utility">Its name as it stands in the kit (<c>simpleFoam</c>).</param>
    /// <returns>The path, whether or not the file exists.</returns>
    public string ExecutablePath(string utility)
    {
        return Path.Combine(AppBin, OperatingSystem.IsWindows() ? utility + ".exe" : utility);
    }

    /// <summary>
    /// Whether the kit carries an executable of that name.
    /// </summary>
    /// <param name="utility">Its name.</param>
    /// <returns>True when the file exists.</returns>
    public bool HasExecutable(string utility)
    {
        return File.Exists(ExecutablePath(utility));
    }

    /// <summary>
    /// The environment of a solver process for one task.
    /// </summary>
    /// <param name="scratchRoot">The task's private scratch (HOME and TMPDIR go inside it).</param>
    /// <returns>Name-value pairs to set on the process.</returns>
    public Dictionary<string, string> EnvironmentFor(string scratchRoot)
    {
        var environment = Environment.Resolve(Root, scratchRoot);

        if (MpiLauncher != null && OperatingSystem.IsWindows())
            environment[MSMPI_BIN] = Path.GetDirectoryName(MpiLauncher) ?? string.Empty;

        return environment;
    }

    private static string? FindMpiLauncher(FoamKitEnvironment environment, string root)
    {
        if (OperatingSystem.IsWindows())
        {
            // MS-MPI is the node's own, never bundled (the Windows kit's
            // KIT.env says KIT_MPI=msmpi-external): the launcher is where
            // Microsoft's installer put it, or there is none.
            var bin = System.Environment.GetEnvironmentVariable(MSMPI_BIN, EnvironmentVariableTarget.Machine)
                      ?? System.Environment.GetEnvironmentVariable(MSMPI_BIN);
            if (string.IsNullOrEmpty(bin))
                return null;

            var mpiexec = Path.Combine(bin, MPIEXEC);
            return File.Exists(mpiexec) ? mpiexec : null;
        }

        foreach (var directory in environment.PathEntries(root))
        {
            var candidate = Path.Combine(directory, MPIRUN);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The kit folder, absolute.</summary>
    public string Root { get; }

    /// <summary>The kit's platform name (<c>linux-x64</c>, <c>macos-arm64</c>, <c>windows-x64</c>).</summary>
    public string Platform { get; }

    /// <summary>The parsed KIT.env.</summary>
    public FoamKitEnvironment Environment { get; }

    /// <summary>The directory of the kit's executables (FOAM_APPBIN), absolute.</summary>
    public string AppBin { get; }

    /// <summary>
    /// Full path of the MPI launcher: the kit's own <c>mpirun</c> on Linux and
    /// macOS, the node's <c>mpiexec.exe</c> on Windows; null when parallel
    /// steps cannot run on this node.
    /// </summary>
    public string? MpiLauncher { get; }

    /// <summary>True when parallel steps can run here.</summary>
    public bool SupportsParallel => MpiLauncher != null;

    #endregion
}
