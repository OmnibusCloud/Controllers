namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>What a value returned under one of a shard's tickets turned out to be.</summary>
public enum CorpusLedgerEntryKind
{
    /// <summary>A result with a verdict on the shard: it takes part in the comparison.</summary>
    Execution = 0,

    /// <summary>The node returned the shard unanswered, with a fault code of its own.</summary>
    Fault = 1,

    /// <summary>The value was dropped: not shaped as a result, naming another shard or another controller version, or at odds with the task.</summary>
    Rejected = 2
}
