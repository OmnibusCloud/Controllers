using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>What a job is estimated to cost, from what is known of its shards' sizes.</summary>
/// <param name="Storage">What the job leaves on the server.</param>
/// <param name="Traffic">What the contributors' links carry.</param>
/// <param name="Counted">Every shard's size as the estimates count it: its own, the mean of the origin's answers, or the cap.</param>
internal sealed record CorpusEstimates(CorpusStorageEstimate Storage, CorpusTrafficEstimate Traffic, IReadOnlyList<long> Counted)
{
    /// <summary>The most each shard may be on the wire as the nodes hold it: its declared size and the slack, the cap without one.</summary>
    public IReadOnlyList<long> Bounds { get; init; } = [];
}
