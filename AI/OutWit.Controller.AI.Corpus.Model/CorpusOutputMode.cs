namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What a run keeps on the server. Append new modes at the end: the value travels on the wire.
/// </summary>
public enum CorpusOutputMode
{
    /// <summary>The text product and the signatures of every shard, the statistics, the content hashes, the report and the integrity document (the manifest's <c>"full"</c>, the default).</summary>
    Full = 0,

    /// <summary>
    /// Statistics and hashes only (the manifest's <c>"stats_and_hashes"</c>): every shard is
    /// processed and hashed as under <see cref="Full"/>, but its text and signatures stay on the node
    /// and are deleted with the task; the server keeps the statistics, the content hashes, the
    /// report and the integrity document. For demonstrations and licensing, where a lab must not
    /// hold third-party text. No signatures, so no near-duplicate merge.
    /// </summary>
    StatsAndHashes = 1
}
