namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>The rounds a shard may be sent out in by <c>CorpusProcess.wit</c>.</summary>
public enum CorpusRoundKind
{
    /// <summary>Every shard once, and the sampled ones on a second machine.</summary>
    First = 0,

    /// <summary>The shards the first round left without a result (a node fault, a rejected result), once more.</summary>
    Repair = 1,

    /// <summary>The shards whose copies disagreed, on three machines.</summary>
    Dispute = 2
}
