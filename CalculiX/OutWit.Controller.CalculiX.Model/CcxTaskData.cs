using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One variant's solve as a self-contained work item — the single argument a
/// Grid.ForEach transformer receives. Node and element counts ride as explicit
/// scalars so work estimation never opens the deck blob.
/// </summary>
[MemoryPackable]
public sealed partial class CcxTaskData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxTaskData task)
            return false;

        return VariantIndex.Is(task.VariantIndex)
               && DeckBlobId.Is(task.DeckBlobId)
               && NodeCount.Is(task.NodeCount)
               && ElementCount.Is(task.ElementCount)
               && Threads.Is(task.Threads)
               && Extraction.Check(task.Extraction);
    }

    public override CcxTaskData Clone()
    {
        return new CcxTaskData
        {
            VariantIndex = VariantIndex,
            DeckBlobId = DeckBlobId,
            NodeCount = NodeCount,
            ElementCount = ElementCount,
            Threads = Threads,
            Extraction = Extraction?.Clone()
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: {NodeCount} nodes, {ElementCount} elements";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Source-table index of the variant. Mandatory in every result mapping:
    /// Grid.ForEach returns results in completion order, never source order.
    /// </summary>
    public int VariantIndex { get; set; }

    /// <summary>Blob id of the variant's complete .inp deck.</summary>
    public Guid DeckBlobId { get; set; }

    /// <summary>Mesh node count, for work estimation without opening the blob.</summary>
    public int NodeCount { get; set; }

    /// <summary>Mesh element count, for work estimation without opening the blob.</summary>
    public int ElementCount { get; set; }

    /// <summary>OMP thread count for the solver process; 0 = all cores of the node.</summary>
    public int Threads { get; set; }

    /// <summary>Responses to extract on the node after the solve; null = automatic set only.</summary>
    public CcxExtractionRequestData? Extraction { get; set; }

    #endregion
}
