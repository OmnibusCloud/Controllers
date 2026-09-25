using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One harvested variant: the sweep's verdict and the node's own result,
/// verbatim, in the member of its family. Nothing of the result is copied or
/// reinterpreted at harvest - and so the family's result is part of this
/// row's wire layout. The manifest is default-mode MemoryPack, whose reader
/// refuses a payload with a member it does not know: a member appended to
/// <see cref="CcxResultData"/> or <see cref="FoamResultData"/>, or to a type
/// either carries, breaks every manifest reader built against the older
/// family model. Such an append ships like an append to this type - every
/// manifest reader updated before a host writes it - and the Sweep.Model
/// wire-layout tests freeze those family types beside the sweep's own.
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
               && Outcome.Is(row.Outcome)
               && CalculiX.Check(row.CalculiX)
               && OpenFOAM.Check(row.OpenFOAM);
    }

    public override SweepManifestRowData Clone()
    {
        return new SweepManifestRowData
        {
            VariantIndex = VariantIndex,
            Outcome = Outcome,
            CalculiX = CalculiX?.Clone(),
            OpenFOAM = OpenFOAM?.Clone()
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: {Outcome}";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>The sweep's verdict on the variant.</summary>
    [MemoryPackOrder(1)]
    public SweepOutcome Outcome { get; set; }

    /// <summary>The CalculiX node's result, verbatim; null in a study of another family.</summary>
    [MemoryPackOrder(2)]
    public CcxResultData? CalculiX { get; set; }

    /// <summary>The OpenFOAM node's result, verbatim; null in a study of another family.</summary>
    [MemoryPackOrder(3)]
    public FoamResultData? OpenFOAM { get; set; }

    #endregion
}
