using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// One execution of one task as the server keeps it while it settles a run: what is compared
/// (the verdict, the exit code and digests of the outputs), what it cost, and where the whole
/// result lies in the spool. Small: the outputs themselves are not held in memory.
/// </summary>
internal sealed class VerifyExecution
{
    #region Properties

    /// <summary>The task.</summary>
    public required int Index { get; init; }

    /// <summary>The verdict the node reported.</summary>
    public required VerifyVerdict Verdict { get; init; }

    /// <summary>The exit code the node reported.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Empty for a verdict; else the reason code of a task the node could not run.</summary>
    public required string Fault { get; init; }

    /// <summary>The digest of stdout and of whether it was cut.</summary>
    public required UInt128 Stdout { get; init; }

    /// <summary>The digest of stderr and of whether it was cut.</summary>
    public required UInt128 Stderr { get; init; }

    /// <summary>The digest of the case records, in order.</summary>
    public required UInt128 Cases { get; init; }

    /// <summary>The fuel the node reported.</summary>
    public required long Fuel { get; init; }

    /// <summary>The peak memory the node reported.</summary>
    public required long PeakMemory { get; init; }

    /// <summary>The wall time the node reported.</summary>
    public required int WallMs { get; init; }

    /// <summary>Where the whole result lies in the spool, or -1 when it was not kept.</summary>
    public long SpoolOffset { get; init; } = -1;

    /// <summary>The length of the whole result in the spool.</summary>
    public int SpoolLength { get; init; }

    /// <summary>Whether the execution is a statement about the task: a verdict, not a fault of the node.</summary>
    public bool HasVerdict => Fault.Length == 0;

    #endregion
}
