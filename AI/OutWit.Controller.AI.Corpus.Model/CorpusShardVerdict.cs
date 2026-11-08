namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// How a shard task ended. Append new verdicts at the end: the value travels on the wire.
/// </summary>
public enum CorpusShardVerdict
{
    /// <summary>The shard was fetched and processed; the product and the signatures were uploaded.</summary>
    Completed = 0,

    /// <summary>The source's origin is not on the task's allowlist; nothing was fetched.</summary>
    OriginRefused = 1,

    /// <summary>The origin could not be fetched (after the retries).</summary>
    FetchFailed = 2,

    /// <summary>The shard grew past the task's byte cap while being fetched; nothing was kept.</summary>
    Oversized = 3,

    /// <summary>The shard's bytes are not a WARC file.</summary>
    Malformed = 4,

    /// <summary>The pipeline's pinned model is not on this node, or does not match its pin.</summary>
    PipelineUnavailable = 5,

    /// <summary>Something else went wrong on the node; the error names it.</summary>
    Failed = 6
}
