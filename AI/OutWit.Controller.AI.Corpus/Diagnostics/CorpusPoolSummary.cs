using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// What the host can say about the pool a Corpus run would use, as totals only: no node is
/// named, listed or told apart.
/// </summary>
public sealed class CorpusPoolSummary : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusPoolSummary other)
            return false;

        return Nodes.Is(other.Nodes)
               && Measured.Is(other.Measured)
               && Workers.Is(other.Workers)
               && Rate.Is(other.Rate, tolerance);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusPoolSummary Clone()
    {
        return new CorpusPoolSummary { Nodes = Nodes, Measured = Measured, Workers = Workers, Rate = Rate };
    }

    #endregion

    #region Properties

    /// <summary>Nodes that can take Corpus work for this submitter now.</summary>
    [ToString]
    public int Nodes { get; init; }

    /// <summary>Of them, the nodes that have measured themselves on the reference shard.</summary>
    [ToString]
    public int Measured { get; init; }

    /// <summary>
    /// Shards the measured nodes process at once, added up (the degree each measured at), or
    /// null when the host does not carry the degree of every measured node: a total over some
    /// of the nodes would read as the pool's.
    /// </summary>
    [ToString]
    public int? Workers { get; init; }

    /// <summary>Megabytes of Common Crawl WET per second of the measured nodes, added up.</summary>
    [ToString]
    public double Rate { get; init; }

    #endregion
}
