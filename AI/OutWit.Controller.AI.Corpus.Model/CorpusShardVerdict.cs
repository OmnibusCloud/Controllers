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

    /// <summary>The origin answered and did not give the shard (after the retries): an HTTP status, bytes that do not match the published hash.</summary>
    FetchFailed = 2,

    /// <summary>The shard grew past the task's byte cap while being fetched; nothing was kept.</summary>
    Oversized = 3,

    /// <summary>The shard's bytes are not a WARC file.</summary>
    Malformed = 4,

    /// <summary>The task's pipeline cannot be run as written (it identifies languages and names no model).</summary>
    PipelineUnavailable = 5,

    /// <summary>The task itself cannot be run (it carries no source or no pipeline).</summary>
    Failed = 6,

    /// <summary>
    /// Not a verdict on the shard: the node met a fault of its own (its disk, its link, an
    /// internal error, the wall-clock backstop of the PDF sandbox) and returns the shard
    /// unanswered, with a fixed code in <see cref="CorpusResultData.Error"/>. The script sends
    /// such shards out once more; the report never shows this value.
    /// </summary>
    NodeFault = 7
}
