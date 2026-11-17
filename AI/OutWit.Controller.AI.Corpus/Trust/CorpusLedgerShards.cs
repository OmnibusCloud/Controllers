using System.Collections;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// A run's shards as a collection that holds none of them: every enumeration reads the run again,
/// one task package at a time, and the count is known without reading.
/// </summary>
/// <param name="count">The shards of the job.</param>
/// <param name="read">Reads the run from its start and hands over each shard as it is judged.</param>
internal sealed class CorpusLedgerShards(int count, Func<IEnumerable<CorpusLedgerShard>> read) : IReadOnlyCollection<CorpusLedgerShard>
{
    #region IReadOnlyCollection

    /// <inheritdoc />
    public IEnumerator<CorpusLedgerShard> GetEnumerator()
    {
        return read().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public int Count => count;

    #endregion
}
