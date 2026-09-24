using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One harvested variant: the sweep's verdict and the node's own result,
/// verbatim, in the member of its family. Nothing of the result is copied or
/// reinterpreted at harvest, so a family's result grows (append-only, in its
/// own model) and the manifest carries the growth without a mapping to keep
/// in step.
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
