namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>How a shard ended once every round is in: what the report says of it.</summary>
public enum CorpusShardOutcome
{
    /// <summary>A result was accepted: it came alone, or the copies agreed, or a majority of three machines returned it.</summary>
    Accepted = 0,

    /// <summary>The copies disagreed and no result was returned by the most machines, two at least, without a tie: out of the counts and the products.</summary>
    Disputed = 1,

    /// <summary>No machine returned a result that could be accepted, after the repair round: the reason is a fixed code.</summary>
    NoResult = 2
}
