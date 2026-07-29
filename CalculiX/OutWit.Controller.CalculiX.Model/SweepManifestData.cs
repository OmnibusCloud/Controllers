using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One harvested variant in the manifest: its state, measured facts, the
/// extracted response row and the artifact blob ids.
/// </summary>
[MemoryPackable]
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
    public int VariantIndex { get; set; }

    /// <summary>True when the solver exited 0 and responses were extracted.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Solver exit code.</summary>
    public int ExitCode { get; set; }

    /// <summary>Measured wall-clock solve time in seconds.</summary>
    public double SolveSeconds { get; set; }

    /// <summary>Blob id of the variant's .frd artifact.</summary>
    public Guid? FrdBlobId { get; set; }

    /// <summary>Blob id of the variant's .dat artifact.</summary>
    public Guid? DatBlobId { get; set; }

    /// <summary>Extracted responses; null for a failed variant.</summary>
    public CcxResponseRowData? ResponseRow { get; set; }

    /// <summary>Solver error tail of a failed variant.</summary>
    public string? LogTail { get; set; }

    #endregion
}

/// <summary>
/// Everything harvested so far, appended chunk by chunk. Uploaded as a fresh
/// blob after every chunk; rows survive job cancellation because a client
/// that has read the manifest owns every blob id in it.
/// </summary>
[MemoryPackable]
public sealed partial class SweepManifestData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepManifestData manifest)
            return false;

        return Rows.IsSequence(manifest.Rows, tolerance);
    }

    public override SweepManifestData Clone()
    {
        return new SweepManifestData
        {
            Rows = Rows.Select(row => row.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"manifest: {Rows.Count} row(s)";
    }

    #endregion

    #region Properties

    /// <summary>Harvested variants in harvest order.</summary>
    public List<SweepManifestRowData> Rows { get; set; } = [];

    #endregion
}
