using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One variant's outcome. A nonzero solver exit is DATA here, not a task
/// failure — a variant that failed to converge must not poison its node batch;
/// the orchestration records it and moves on.
/// </summary>
[MemoryPackable]
public sealed partial class CcxResultData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxResultData result)
            return false;

        return VariantIndex.Is(result.VariantIndex)
               && FrdBlobId.Is(result.FrdBlobId)
               && DatBlobId.Is(result.DatBlobId)
               && ExitCode.Is(result.ExitCode)
               && SolveSeconds.Is(result.SolveSeconds, tolerance)
               && ResponseRow.Check(result.ResponseRow)
               && LogTail.Is(result.LogTail);
    }

    public override CcxResultData Clone()
    {
        return new CcxResultData
        {
            VariantIndex = VariantIndex,
            FrdBlobId = FrdBlobId,
            DatBlobId = DatBlobId,
            ExitCode = ExitCode,
            SolveSeconds = SolveSeconds,
            ResponseRow = ResponseRow?.Clone(),
            LogTail = LogTail
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: exit {ExitCode}, {SolveSeconds:F1} s";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant this result belongs to.</summary>
    public int VariantIndex { get; set; }

    /// <summary>Blob id of the uploaded .frd result file; null when the solve produced none.</summary>
    public Guid? FrdBlobId { get; set; }

    /// <summary>Blob id of the uploaded .dat result file; null when the solve produced none.</summary>
    public Guid? DatBlobId { get; set; }

    /// <summary>Solver process exit code; 0 = success.</summary>
    public int ExitCode { get; set; }

    /// <summary>Measured wall-clock solve time in seconds.</summary>
    public double SolveSeconds { get; set; }

    /// <summary>Responses extracted on the node; null when the solve failed.</summary>
    public CcxResponseRowData? ResponseRow { get; set; }

    /// <summary>Last lines of solver output — the error tail of a failed variant.</summary>
    public string? LogTail { get; set; }

    #endregion
}
