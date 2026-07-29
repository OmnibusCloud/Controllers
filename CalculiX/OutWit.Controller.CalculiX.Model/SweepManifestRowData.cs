using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One harvested variant in the manifest: its state, measured facts, the
/// extracted response row and the artifact blob ids.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepManifestRowData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepManifestRowData row)
            return false;

        return VariantIndex.Is(row.VariantIndex)
               && Succeeded.Is(row.Succeeded)
               && ExitCode.Is(row.ExitCode)
               && SolveSeconds.Is(row.SolveSeconds, tolerance)
               && FrdBlobId.Is(row.FrdBlobId)
               && DatBlobId.Is(row.DatBlobId)
               && ResponseRow.Check(row.ResponseRow)
               && LogTail.Is(row.LogTail);
    }

    public override SweepManifestRowData Clone()
    {
        return new SweepManifestRowData
        {
            VariantIndex = VariantIndex,
            Succeeded = Succeeded,
            ExitCode = ExitCode,
            SolveSeconds = SolveSeconds,
            FrdBlobId = FrdBlobId,
            DatBlobId = DatBlobId,
            ResponseRow = ResponseRow?.Clone(),
            LogTail = LogTail
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: {(Succeeded ? "done" : $"failed ({ExitCode})")}";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>True when the solver exited 0 and responses were extracted.</summary>
    [MemoryPackOrder(1)]
    public bool Succeeded { get; set; }

    /// <summary>Solver exit code.</summary>
    [MemoryPackOrder(2)]
    public int ExitCode { get; set; }

    /// <summary>Measured wall-clock solve time in seconds.</summary>
    [MemoryPackOrder(3)]
    public double SolveSeconds { get; set; }

    /// <summary>Blob id of the variant's .frd artifact.</summary>
    [MemoryPackOrder(4)]
    public Guid? FrdBlobId { get; set; }

    /// <summary>Blob id of the variant's .dat artifact.</summary>
    [MemoryPackOrder(5)]
    public Guid? DatBlobId { get; set; }

    /// <summary>Extracted responses; null for a failed variant.</summary>
    [MemoryPackOrder(6)]
    public CcxResponseRowData? ResponseRow { get; set; }

    /// <summary>Solver error tail of a failed variant.</summary>
    [MemoryPackOrder(7)]
    public string? LogTail { get; set; }

    #endregion
}
