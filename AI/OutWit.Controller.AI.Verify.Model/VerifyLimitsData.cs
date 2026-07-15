using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Per-task resource envelope. A value of 0 means "unset — fall back to the batch
/// default, then to the sandbox default"; server-side ceilings are applied in preflight.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyLimitsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyLimitsData other)
            return false;

        return FuelBudget.Is(other.FuelBudget)
               && MemoryBytes.Is(other.MemoryBytes)
               && WallTimeMs.Is(other.WallTimeMs)
               && StdoutLimitBytes.Is(other.StdoutLimitBytes)
               && StderrLimitBytes.Is(other.StderrLimitBytes);
    }

    public override VerifyLimitsData Clone()
    {
        return new VerifyLimitsData
        {
            FuelBudget = FuelBudget,
            MemoryBytes = MemoryBytes,
            WallTimeMs = WallTimeMs,
            StdoutLimitBytes = StdoutLimitBytes,
            StderrLimitBytes = StderrLimitBytes
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// CPU budget in wasmtime fuel units (≈ executed instructions). Fuel is a
    /// deterministic budget mechanism; it is not used for integrity comparison.
    /// Python-scale reference: a hello-world burns ~1.6e8 fuel.
    /// </summary>
    [ToString]
    [MemoryPackOrder(0)]
    public long FuelBudget { get; set; }

    /// <summary>Linear-memory cap in bytes for one execution's store.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public long MemoryBytes { get; set; }

    /// <summary>Wall-clock cap in milliseconds, enforced via epoch interruption.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int WallTimeMs { get; set; }

    /// <summary>Stdout cap in bytes; exceeding it yields the OutputExceeded verdict.</summary>
    [MemoryPackOrder(3)]
    public int StdoutLimitBytes { get; set; }

    /// <summary>Stderr cap in bytes; overflow is truncated (stderr never fails a task by itself).</summary>
    [MemoryPackOrder(4)]
    public int StderrLimitBytes { get; set; }

    #endregion
}
