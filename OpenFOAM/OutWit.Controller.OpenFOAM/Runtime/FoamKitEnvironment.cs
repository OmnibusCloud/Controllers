namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The kit's KIT.env: the exact environment the kit's build established,
/// recorded by the kit's own pack step with the kit folder as @KIT@ and the
/// scratch as @SCRATCH@. The controller substitutes both and sets the result
/// on the solver process - nothing on a node ever sources anything, and the
/// process inherits nothing from the node service beyond the system part of
/// PATH, which this class appends.
/// </summary>
public sealed class FoamKitEnvironment
{
    #region Constants

    /// <summary>Placeholder of the kit folder in KIT.env.</summary>
    public const string KIT_TOKEN = "@KIT@";

    /// <summary>Placeholder of the task's private scratch in KIT.env.</summary>
    public const string SCRATCH_TOKEN = "@SCRATCH@";

    /// <summary>The file's name at the kit root.</summary>
    public const string FILE_NAME = "KIT.env";

    /// <summary>The variable naming the directories whose files are marked executable after unpacking (Linux, macOS).</summary>
    public const string EXECUTABLE_DIRS = "KIT_EXECUTABLE_DIRS";

    /// <summary>The variable naming the kit's platform (linux-x64, macos-arm64, windows-x64).</summary>
    public const string PLATFORM = "KIT_PLATFORM";

    private const string PATH = "PATH";

    #endregion

    #region Constructors

    private FoamKitEnvironment(IReadOnlyList<KeyValuePair<string, string>> entries)
    {
        Entries = entries;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads a KIT.env file.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The parsed environment.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file names no WM_PROJECT_DIR or no PATH.</exception>
    public static FoamKitEnvironment Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The kit carries no KIT.env.", path);

        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Parses KIT.env text: one NAME=VALUE per line, '#' lines are comments,
    /// order kept.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The parsed environment.</returns>
    /// <exception cref="InvalidDataException">The text names no WM_PROJECT_DIR or no PATH.</exception>
    public static FoamKitEnvironment Parse(string text)
    {
        var entries = new List<KeyValuePair<string, string>>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..];
            entries.RemoveAll(entry => entry.Key == name);
            entries.Add(new KeyValuePair<string, string>(name, value));
        }

        var environment = new FoamKitEnvironment(entries);
        if (environment.Get("WM_PROJECT_DIR") == null)
            throw new InvalidDataException("KIT.env names no WM_PROJECT_DIR.");
        if (environment.Get(PATH) == null)
            throw new InvalidDataException("KIT.env names no PATH.");

        return environment;
    }

    /// <summary>
    /// The raw value of a variable, placeholders unsubstituted.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The value, or null when KIT.env does not set it.</returns>
    public string? Get(string name)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key == name)
                return entry.Value;
        }

        return null;
    }

    /// <summary>
    /// A variable's value with the kit folder substituted; the scratch
    /// placeholder, if any, is left in place.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="kitRoot">The kit folder.</param>
    /// <returns>The value, or null when KIT.env does not set it.</returns>
    public string? Get(string name, string kitRoot)
    {
        return Get(name)?.Replace(KIT_TOKEN, ToKitPath(kitRoot));
    }

    /// <summary>
    /// The complete environment of a solver process: every KIT.env variable
    /// with both placeholders substituted, the system directories appended to
    /// PATH, and on Windows SystemRoot passed through (the C runtime needs it).
    /// </summary>
    /// <param name="kitRoot">The kit folder.</param>
    /// <param name="scratchRoot">The task's private scratch.</param>
    /// <returns>Name-value pairs to set on the process, in KIT.env order.</returns>
    public Dictionary<string, string> Resolve(string kitRoot, string scratchRoot)
    {
        var kit = ToKitPath(kitRoot);
        var scratch = ToKitPath(scratchRoot);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in Entries)
            result[entry.Key] = entry.Value.Replace(KIT_TOKEN, kit).Replace(SCRATCH_TOKEN, scratch);

        result[PATH] = result[PATH] + Path.PathSeparator + SystemPath();

        if (OperatingSystem.IsWindows())
        {
            var systemRoot = System.Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            result["SystemRoot"] = systemRoot;
        }

        return result;
    }

    /// <summary>
    /// The directories whose files the controller marks executable after
    /// unpacking (a zip keeps no mode bits). Empty on Windows kits.
    /// </summary>
    /// <param name="kitRoot">The kit folder.</param>
    /// <returns>Absolute directories, in KIT.env order.</returns>
    public IReadOnlyList<string> ExecutableDirectories(string kitRoot)
    {
        return SplitList(EXECUTABLE_DIRS, kitRoot);
    }

    /// <summary>
    /// The kit entries of PATH, as absolute directories.
    /// </summary>
    /// <param name="kitRoot">The kit folder.</param>
    /// <returns>Directories, in PATH order.</returns>
    public IReadOnlyList<string> PathEntries(string kitRoot)
    {
        return SplitList(PATH, kitRoot);
    }

    /// <summary>
    /// The system part of PATH the controller appends: where the platform's
    /// own tools live and nothing else. A node service's PATH is never inherited.
    /// </summary>
    /// <returns>A PATH fragment.</returns>
    public static string SystemPath()
    {
        if (!OperatingSystem.IsWindows())
            return "/usr/local/bin:/usr/bin:/bin";

        var systemRoot = System.Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        return $@"{systemRoot}\System32;{systemRoot}";
    }

    /// <summary>
    /// Splits a list-valued variable. The RAW value is split, before the kit
    /// folder is substituted: it holds @KIT@-relative entries and no drive
    /// letters, so the separator is whichever the kit's platform wrote (':'
    /// on Linux and macOS, ';' on Windows), and a Windows kit folder such as
    /// C:/kits/... cannot be mistaken for two entries.
    /// </summary>
    private IReadOnlyList<string> SplitList(string name, string kitRoot)
    {
        var raw = Get(name);
        if (string.IsNullOrEmpty(raw))
            return [];

        var separator = raw.Contains(';') ? ';' : ':';
        var kit = ToKitPath(kitRoot);

        return raw
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Replace(KIT_TOKEN, kit))
            .ToList();
    }

    private static string ToKitPath(string path)
    {
        // KIT.env is written with forward slashes on every platform; Windows
        // accepts them everywhere the kit uses them, and a mixed path stays
        // readable in the logs.
        return path.TrimEnd('/', '\\').Replace('\\', '/');
    }

    #endregion

    #region Properties

    /// <summary>Every NAME=VALUE of the file, raw, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries { get; }

    #endregion
}
