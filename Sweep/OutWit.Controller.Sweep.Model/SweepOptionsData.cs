using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// A parameter study as the initiator submits it: the study itself - the
/// parameters, the variant table, the chunk progression bounds - and exactly
/// one family block carrying what the variants run on. Nothing family-specific
/// sits outside its block, so the study, the plan, the chunking and the state
/// are one implementation for every solver family.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepOptionsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepOptionsData options)
            return false;

        return Parameters.IsSequence(options.Parameters, tolerance)
               && Variants.IsSequence(options.Variants, tolerance)
               && FirstChunkSize.Is(options.FirstChunkSize)
               && MaxChunkSize.Is(options.MaxChunkSize)
               && CalculiX.Check(options.CalculiX)
               && OpenFOAM.Check(options.OpenFOAM);
    }

    public override SweepOptionsData Clone()
    {
        return new SweepOptionsData
        {
            Parameters = Parameters.Select(parameter => parameter.Clone()).ToList(),
            Variants = Variants.Select(variant => variant.Clone()).ToList(),
            FirstChunkSize = FirstChunkSize,
            MaxChunkSize = MaxChunkSize,
            CalculiX = CalculiX?.Clone(),
            OpenFOAM = OpenFOAM?.Clone()
        };
    }

    public override string ToString()
    {
        var family = Family?.ToString() ?? "no family";
        return $"{family} sweep: {Parameters.Count} parameter(s) x {Variants.Count} variant(s)";
    }

    #endregion

    #region Properties

    /// <summary>Swept parameters, in token order.</summary>
    [MemoryPackOrder(0)]
    public List<SweepParameterData> Parameters { get; set; } = [];

    /// <summary>The variant table.</summary>
    [MemoryPackOrder(1)]
    public List<SweepVariantData> Variants { get; set; } = [];

    /// <summary>First chunk size of the progressive schedule; 0 = server default. The fleet's width raises it.</summary>
    [MemoryPackOrder(2)]
    public int FirstChunkSize { get; set; }

    /// <summary>Chunk size cap of the progressive schedule; 0 = server default.</summary>
    [MemoryPackOrder(3)]
    public int MaxChunkSize { get; set; }

    /// <summary>The CalculiX input (a template deck or a deck set); null unless the study runs on CalculiX.</summary>
    [MemoryPackOrder(4)]
    public SweepCalculiXStudyData? CalculiX { get; set; }

    /// <summary>The OpenFOAM case every variant runs with its own token values; null unless the study runs on OpenFOAM.</summary>
    [MemoryPackOrder(5)]
    public FoamCaseData? OpenFOAM { get; set; }

    /// <summary>
    /// The family whose block is set; null when none is, or more than one -
    /// a study the plan refuses.
    /// </summary>
    [MemoryPackIgnore]
    public SweepFamily? Family
    {
        get
        {
            if (CalculiX != null && OpenFOAM == null)
                return SweepFamily.CalculiX;
            if (OpenFOAM != null && CalculiX == null)
                return SweepFamily.OpenFOAM;
            return null;
        }
    }

    #endregion
}
