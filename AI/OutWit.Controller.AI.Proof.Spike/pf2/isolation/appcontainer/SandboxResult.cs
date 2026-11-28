namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// How one sandboxed run ended and what it cost, from the Job Object's own accounting.
/// </summary>
public sealed class SandboxResult
{
    #region Properties

    /// <summary>The command line.</summary>
    public string CommandLine { get; init; } = "";

    /// <summary>Whether the process ran in the AppContainer.</summary>
    public bool Container { get; init; }

    /// <summary>Whether the AppContainer was a less-privileged one.</summary>
    public bool Lpac { get; init; }

    /// <summary>The console the process was given (detached, hidden, new).</summary>
    public string Console { get; init; } = "";

    /// <summary>The AppContainer's SID (null without a container).</summary>
    public string? ContainerSid { get; init; }

    /// <summary>The exit code of the first process (null when it could not start).</summary>
    public long? ExitCode { get; set; }

    /// <summary>accepted (exit 0), rejected, crashed, memory_cap, wall_cap, could_not_start.</summary>
    public string Ending { get; set; } = "";

    /// <summary>The Win32 error when the process could not start.</summary>
    public string? StartError { get; set; }

    /// <summary>Wall seconds from the resume to the end.</summary>
    public double WallSeconds { get; set; }

    /// <summary>Seconds spent granting the folders to the container (icacls).</summary>
    public double GrantSeconds { get; set; }

    /// <summary>The peak commit of the whole job (every process of the tree).</summary>
    public ulong PeakJobMemory { get; set; }

    /// <summary>The peak commit of its largest process.</summary>
    public ulong PeakProcessMemory { get; set; }

    /// <summary>User CPU seconds of the whole job.</summary>
    public double UserSeconds { get; set; }

    /// <summary>Kernel CPU seconds of the whole job.</summary>
    public double KernelSeconds { get; set; }

    /// <summary>Processes the job held over its life.</summary>
    public uint TotalProcesses { get; set; }

    /// <summary>Processes still alive when the first one ended (then ended with the job).</summary>
    public uint LeftOver { get; set; }

    /// <summary>The memory cap in bytes (0: none).</summary>
    public ulong MemoryCap { get; init; }

    /// <summary>The wall cap in seconds.</summary>
    public double WallCap { get; init; }

    /// <summary>The end of the standard output.</summary>
    public string Stdout { get; set; } = "";

    /// <summary>The end of the standard error.</summary>
    public string Stderr { get; set; } = "";

    #endregion
}
