using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Job-level knobs consumed by the host-side activities (Split applies defaults and
/// chunking; Collect shapes the report; integrity sampling rate feeds re-execution).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyOptionsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyOptionsData other)
            return false;

        return DefaultLimits.Check(other.DefaultLimits)
               && BatchSize.Is(other.BatchSize)
               && SamplingRate.Is(other.SamplingRate, tolerance)
               && ReportFormat.Is(other.ReportFormat);
    }

    public override VerifyOptionsData Clone()
    {
        return new VerifyOptionsData
        {
            DefaultLimits = DefaultLimits?.Clone(),
            BatchSize = BatchSize,
            SamplingRate = SamplingRate,
            ReportFormat = ReportFormat
        };
    }

    #endregion

    #region Properties

    /// <summary>Limit defaults for every task that does not override them.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(0)]
    public VerifyLimitsData? DefaultLimits { get; set; }

    /// <summary>Tasks per chunk (0 — let Split decide).</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int BatchSize { get; set; }

    /// <summary>Fraction of tasks re-executed for integrity sampling (0.0–1.0).</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public double SamplingRate { get; set; }

    /// <summary>Verdict report format assembled by Collect.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public VerifyReportFormat ReportFormat { get; set; }

    #endregion
}
