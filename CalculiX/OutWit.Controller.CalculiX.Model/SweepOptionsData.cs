using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One swept parameter: a display name and the placeholder token baked into
/// the base deck in place of the original numeric literal.
/// </summary>
[MemoryPackable]
public sealed partial class SweepParameterData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepParameterData parameter)
            return false;

        return Name.Is(parameter.Name)
               && Token.Is(parameter.Token);
    }

    public override SweepParameterData Clone()
    {
        return new SweepParameterData
        {
            Name = Name,
            Token = Token
        };
    }

    public override string ToString()
    {
        return $"{Name} ({Token})";
    }

    #endregion

    #region Properties

    /// <summary>Display name of the parameter.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Placeholder token present in the base deck, e.g. "{{oc1}}".</summary>
    public string Token { get; set; } = string.Empty;

    #endregion
}

/// <summary>
/// One row of the variant table: the substitution values, one per parameter,
/// in the parameter list's order.
/// </summary>
[MemoryPackable]
public sealed partial class SweepVariantData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepVariantData variant)
            return false;

        return VariantIndex.Is(variant.VariantIndex)
               && Values.Is(variant.Values);
    }

    public override SweepVariantData Clone()
    {
        return new SweepVariantData
        {
            VariantIndex = VariantIndex,
            Values = [.. Values]
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: [{string.Join(", ", Values)}]";
    }

    #endregion

    #region Properties

    /// <summary>Stable index of the variant in the study.</summary>
    public int VariantIndex { get; set; }

    /// <summary>Substitution values, ordered like the parameter list.</summary>
    public List<string> Values { get; set; } = [];

    #endregion
}

/// <summary>
/// A parameter study as submitted by the client: the parameter list, the
/// variant table, the extraction request, the thread policy and the chunk
/// progression bounds (the client sizes them from the fleet width it can
/// query; zero means server default).
/// </summary>
[MemoryPackable]
public sealed partial class SweepOptionsData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepOptionsData options)
            return false;

        return Parameters.IsSequence(options.Parameters, tolerance)
               && Variants.IsSequence(options.Variants, tolerance)
               && Extraction.Check(options.Extraction)
               && Threads.Is(options.Threads)
               && NodeCount.Is(options.NodeCount)
               && ElementCount.Is(options.ElementCount)
               && FirstChunkSize.Is(options.FirstChunkSize)
               && MaxChunkSize.Is(options.MaxChunkSize);
    }

    public override SweepOptionsData Clone()
    {
        return new SweepOptionsData
        {
            Parameters = Parameters.Select(parameter => parameter.Clone()).ToList(),
            Variants = Variants.Select(variant => variant.Clone()).ToList(),
            Extraction = Extraction?.Clone(),
            Threads = Threads,
            NodeCount = NodeCount,
            ElementCount = ElementCount,
            FirstChunkSize = FirstChunkSize,
            MaxChunkSize = MaxChunkSize
        };
    }

    public override string ToString()
    {
        return $"sweep: {Parameters.Count} parameter(s) x {Variants.Count} variant(s)";
    }

    #endregion

    #region Properties

    /// <summary>Swept parameters, in token order.</summary>
    public List<SweepParameterData> Parameters { get; set; } = [];

    /// <summary>The variant table.</summary>
    public List<SweepVariantData> Variants { get; set; } = [];

    /// <summary>Responses to extract per variant; null = automatic set only.</summary>
    public CcxExtractionRequestData? Extraction { get; set; }

    /// <summary>OMP thread count per solve; 0 = all cores of the executing node.</summary>
    public int Threads { get; set; }

    /// <summary>Mesh node count of the base deck (same mesh for every variant).</summary>
    public int NodeCount { get; set; }

    /// <summary>Mesh element count of the base deck (same mesh for every variant).</summary>
    public int ElementCount { get; set; }

    /// <summary>First chunk size of the progressive schedule; 0 = server default.</summary>
    public int FirstChunkSize { get; set; }

    /// <summary>Chunk size cap of the progressive schedule; 0 = server default.</summary>
    public int MaxChunkSize { get; set; }

    #endregion
}
