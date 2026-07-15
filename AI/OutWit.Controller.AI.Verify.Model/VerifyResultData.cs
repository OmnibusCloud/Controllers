using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// One execution's outcome. Verdict-first and kilobyte-scale by design: stdout/stderr
/// are truncated inline at the task's caps. Metrics (fuel, memory, wall) legitimately
/// vary across nodes — integrity byte-comparison uses <see cref="IsComparable"/> fields only.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyResultData : ModelBase
{
    #region Functions

    /// <summary>
    /// Deterministic-fields comparison for integrity sampling: two honest executions of
    /// the same task must agree on these regardless of node speed. Metrics are excluded
    /// (fuel is clock-sensitive at the margin; wall time is hardware-dependent).
    /// </summary>
    public bool IsComparable(VerifyResultData other)
    {
        if (CaseResults.Count != other.CaseResults.Count)
            return false;

        for (var i = 0; i < CaseResults.Count; i++)
        {
            if (!CaseResults[i].Is(other.CaseResults[i]))
                return false;
        }

        return TaskIndex.Is(other.TaskIndex)
               && Verdict.Is(other.Verdict)
               && ExitCode.Is(other.ExitCode)
               && Stdout.Is(other.Stdout)
               && StdoutTruncated.Is(other.StdoutTruncated)
               && Stderr.Is(other.Stderr)
               && StderrTruncated.Is(other.StderrTruncated);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyResultData other)
            return false;

        return IsComparable(other)
               && FuelConsumed.Is(other.FuelConsumed)
               && PeakMemoryBytes.Is(other.PeakMemoryBytes)
               && WallMs.Is(other.WallMs);
    }

    public override VerifyResultData Clone()
    {
        return new VerifyResultData
        {
            TaskIndex = TaskIndex,
            Verdict = Verdict,
            ExitCode = ExitCode,
            Stdout = Stdout,
            StdoutTruncated = StdoutTruncated,
            Stderr = Stderr,
            StderrTruncated = StderrTruncated,
            CaseResults = CaseResults.Select(c => c.Clone()).ToList(),
            FuelConsumed = FuelConsumed,
            PeakMemoryBytes = PeakMemoryBytes,
            WallMs = WallMs
        };
    }

    #endregion

    #region Properties

    /// <summary>Task index this result answers (matches <see cref="VerifyTaskData.TaskIndex"/>).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int TaskIndex { get; set; }

    /// <summary>The verdict — what an RLVR/eval pipeline consumes.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public VerifyVerdict Verdict { get; set; }

    /// <summary>Exit code of the suite-less run, or of the first failing case.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int ExitCode { get; set; }

    /// <summary>Stdout of the suite-less run (per-case stdout lives in <see cref="CaseResults"/>), capped.</summary>
    [MemoryPackOrder(3)]
    public string Stdout { get; set; } = "";

    /// <summary>True when stdout exceeded its cap and was truncated.</summary>
    [MemoryPackOrder(4)]
    public bool StdoutTruncated { get; set; }

    /// <summary>Stderr (last run's), capped.</summary>
    [MemoryPackOrder(5)]
    public string Stderr { get; set; } = "";

    /// <summary>True when stderr exceeded its cap and was truncated.</summary>
    [MemoryPackOrder(6)]
    public bool StderrTruncated { get; set; }

    /// <summary>Per-case outcomes when the task carries a suite; empty otherwise.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(7)]
    public List<VerifyCaseResultData> CaseResults { get; set; } = [];

    /// <summary>Total fuel consumed across the task's runs (budget accounting, not integrity).</summary>
    [MemoryPackOrder(8)]
    public long FuelConsumed { get; set; }

    /// <summary>Final linear-memory size of the heaviest run — WASM memory only grows, so this is the peak.</summary>
    [MemoryPackOrder(9)]
    public long PeakMemoryBytes { get; set; }

    /// <summary>Wall-clock milliseconds across the task's runs.</summary>
    [MemoryPackOrder(10)]
    public int WallMs { get; set; }

    #endregion
}
