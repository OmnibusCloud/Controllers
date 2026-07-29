using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// A parameter study as submitted by the client: the parameter list, the
/// variant table, the extraction request, the thread policy and the chunk
/// progression bounds (the client sizes them from the fleet width it can
/// query; zero means server default).
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
    [MemoryPackOrder(0)]
    public List<SweepParameterData> Parameters { get; set; } = [];

    /// <summary>The variant table.</summary>
    [MemoryPackOrder(1)]
    public List<SweepVariantData> Variants { get; set; } = [];

    /// <summary>Responses to extract per variant; null = automatic set only.</summary>
    [MemoryPackOrder(2)]
    public CcxExtractionRequestData? Extraction { get; set; }

    /// <summary>OMP thread count per solve; 0 = all cores of the executing node.</summary>
    [MemoryPackOrder(3)]
    public int Threads { get; set; }

    /// <summary>Mesh node count of the base deck (same mesh for every variant).</summary>
    [MemoryPackOrder(4)]
    public int NodeCount { get; set; }

    /// <summary>Mesh element count of the base deck (same mesh for every variant).</summary>
    [MemoryPackOrder(5)]
    public int ElementCount { get; set; }

    /// <summary>First chunk size of the progressive schedule; 0 = server default.</summary>
    [MemoryPackOrder(6)]
    public int FirstChunkSize { get; set; }

    /// <summary>Chunk size cap of the progressive schedule; 0 = server default.</summary>
    [MemoryPackOrder(7)]
    public int MaxChunkSize { get; set; }

    #endregion
}
