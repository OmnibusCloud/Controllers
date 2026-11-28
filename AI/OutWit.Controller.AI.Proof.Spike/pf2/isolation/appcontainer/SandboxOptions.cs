namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// What one sandboxed run is allowed and capped at.
/// </summary>
public sealed class SandboxOptions
{
    #region Functions

    /// <summary>
    /// Reads the options of <c>run</c> and <c>probe</c>; the command follows <c>--</c>.
    /// </summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An unknown option or a missing value.</exception>
    public static SandboxOptions Parse(IReadOnlyList<string> args)
    {
        var options = new SandboxOptions();
        for (var i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            switch (args[i])
            {
                case "--name": options.Name = Value(); break;
                case "--job-dir": options.JobDir = Path.GetFullPath(Value()); break;
                case "--grant-read": options.ReadDirs.Add(Path.GetFullPath(Value())); break;
                case "--memory-mb": options.MemoryMb = long.Parse(Value()); break;
                case "--wall-s": options.WallSeconds = double.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--processes": options.Processes = int.Parse(Value()); break;
                case "--no-container": options.Container = false; break;
                case "--lpac": options.Lpac = true; break;
                case "--no-children": options.NoChildren = true; break;
                case "--stdout": options.StdoutPath = Path.GetFullPath(Value()); break;
                case "--stderr": options.StderrPath = Path.GetFullPath(Value()); break;
                case "--env": options.Environment.Add(Value()); break;
                case "--outside-dir": options.OutsideDir = Path.GetFullPath(Value()); break;
                case "--console": options.Console = Value(); break;
                case "--default-dacl": options.DefaultDacl = true; break;
                case "--filtered": options.Filtered = true; break;
                case "--": options.Command.AddRange(args.Skip(i + 1)); i = args.Count; break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }
        return options;
    }

    #endregion

    #region Properties

    /// <summary>The AppContainer profile's name.</summary>
    public string Name { get; private set; } = "pf2-kernel";

    /// <summary>The one folder the process may write (and read): the job's.</summary>
    public string JobDir { get; private set; } = Directory.GetCurrentDirectory();

    /// <summary>Folders the process may read and execute from (the toolchain).</summary>
    public List<string> ReadDirs { get; } = [];

    /// <summary>The Job Object's memory cap for the whole tree, in MiB (0: none).</summary>
    public long MemoryMb { get; private set; }

    /// <summary>The wall cap in seconds; the whole job is ended at it.</summary>
    public double WallSeconds { get; private set; } = 3600;

    /// <summary>How many processes the job may hold at once (0: no limit).</summary>
    public int Processes { get; private set; }

    /// <summary>Whether the process runs in the AppContainer (false: the Job Object only).</summary>
    public bool Container { get; private set; } = true;

    /// <summary>A less-privileged AppContainer: opted out of what ALL APPLICATION PACKAGES may reach.</summary>
    public bool Lpac { get; private set; }

    /// <summary>Whether the process may start no child process.</summary>
    public bool NoChildren { get; private set; }

    /// <summary>Where the process's standard output goes.</summary>
    public string? StdoutPath { get; private set; }

    /// <summary>Where the process's standard error goes.</summary>
    public string? StderrPath { get; private set; }

    /// <summary>Extra environment variables (NAME=VALUE); the block is otherwise minimal.</summary>
    public List<string> Environment { get; } = [];

    /// <summary>For <c>probe</c>: a folder outside the job's, with a file the probe must not read.</summary>
    public string? OutsideDir { get; private set; }

    /// <summary>
    /// The console the process gets: <c>detached</c> (none; a console child then needs a console of its
    /// own), <c>hidden</c> (a console without a window, inherited by children) or <c>new</c> (a new,
    /// hidden console window).
    /// </summary>
    public string Console { get; private set; } = "detached";

    /// <summary>
    /// Whether the container's token gets a default DACL that grants the container's own SID, so
    /// that the objects it creates without a descriptor (a pipe of <c>CreatePipe</c>) are its own.
    /// </summary>
    public bool DefaultDacl { get; private set; }

    /// <summary>
    /// Whether the process starts from a filtered token (as User Account Control gives an
    /// administrator's programs: Administrators for deny only, no privileges, medium integrity).
    /// </summary>
    public bool Filtered { get; private set; }

    /// <summary>The command and its arguments.</summary>
    public List<string> Command { get; } = [];

    #endregion
}
