using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Accumulates a shard's counters while it streams and emits them in a canonical order
/// (rule names and language codes sorted ordinally) so two nodes' statistics for one shard
/// compare equal field by field.
/// </summary>
public sealed class ShardStatsBuilder
{
    #region Fields

    private readonly Dictionary<string, int> m_drops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_languages = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Counts one record read from the shard.</summary>
    public void Record()
    {
        RecordsIn++;
    }

    /// <summary>Counts one document that yielded text.</summary>
    public void Extracted()
    {
        DocumentsExtracted++;
    }

    /// <summary>Counts one document dropped by a rule.</summary>
    /// <param name="rule">The rule name.</param>
    public void Drop(string rule)
    {
        m_drops[rule] = m_drops.GetValueOrDefault(rule) + 1;
    }

    /// <summary>Counts one document in the product.</summary>
    /// <param name="languageCode">Its language ("" when the stage is off).</param>
    public void Out(string languageCode)
    {
        DocumentsOut++;
        m_languages[languageCode] = m_languages.GetValueOrDefault(languageCode) + 1;
    }

    /// <summary>The statistics, counts in canonical order, byte and time fields as given.</summary>
    /// <param name="bytesFetched">Bytes fetched from the origin.</param>
    /// <param name="bytesProduced">Bytes of the compressed product.</param>
    /// <param name="fetchMs">Wall time of the fetch.</param>
    /// <param name="processMs">Wall time of the processing.</param>
    /// <param name="waitMs">Wall time spent waiting for the node's download and processing slots.</param>
    /// <returns>The statistics.</returns>
    public CorpusShardStatsData Build(long bytesFetched, long bytesProduced, int fetchMs, int processMs, int waitMs = 0)
    {
        return new CorpusShardStatsData
        {
            RecordsIn = RecordsIn,
            DocumentsExtracted = DocumentsExtracted,
            DocumentsOut = DocumentsOut,
            Drops = Sorted(m_drops),
            Languages = Sorted(m_languages),
            BytesFetched = bytesFetched,
            BytesProduced = bytesProduced,
            FetchMs = fetchMs,
            ProcessMs = processMs,
            WaitMs = waitMs
        };
    }

    private static List<CorpusCountData> Sorted(Dictionary<string, int> counts)
    {
        return counts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CorpusCountData { Name = pair.Key, Count = pair.Value })
            .ToList();
    }

    #endregion

    #region Properties

    /// <summary>Records read so far.</summary>
    public int RecordsIn { get; private set; }

    /// <summary>Documents that yielded text so far.</summary>
    public int DocumentsExtracted { get; private set; }

    /// <summary>Documents in the product so far.</summary>
    public int DocumentsOut { get; private set; }

    #endregion
}
