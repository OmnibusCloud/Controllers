using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Accumulates a shard's counters while it streams and emits them in a canonical order
/// (rule names and language codes sorted ordinally) so two nodes' statistics for one shard
/// compare equal field by field.
/// </summary>
public sealed class ShardStatsBuilder
{
    #region Constants

    /// <summary>The breakdown key of a document past the limit of its nearest supported language: <c>past_limit:</c> and that language's code.</summary>
    public const string UNSUPPORTED_PAST_LIMIT = "past_limit:";

    /// <summary>The breakdown key of a document that shares no n-gram with any profile: its nearest profile says nothing.</summary>
    public const string UNSUPPORTED_NO_PROFILE = "no_profile";

    #endregion

    #region Fields

    private readonly Dictionary<string, int> m_drops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_languages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_unsupported = new(StringComparer.Ordinal);
    private readonly CorpusPdfStatsData m_pdf = new();

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

    /// <summary>
    /// Counts one document dropped as <see cref="ShardProcessor.DROP_LANGUAGE_UNSUPPORTED"/>, and
    /// in the breakdown of those drops under <see cref="UnsupportedKey"/>.
    /// </summary>
    /// <param name="guess">The identifier's unsupported verdict.</param>
    public void DropUnsupported(LanguageGuess guess)
    {
        Drop(ShardProcessor.DROP_LANGUAGE_UNSUPPORTED);
        var key = UnsupportedKey(guess);
        m_unsupported[key] = m_unsupported.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Where an unsupported document goes in the breakdown: the code of its nearest profile when
    /// that is a language the pack knows and does not support (<c>swe</c>);
    /// <see cref="UNSUPPORTED_PAST_LIMIT"/> and the code when the nearest profile is a supported
    /// language the document lies past the limit of (<c>past_limit:spa</c>: a language the pack
    /// has no profile for, or text too unlike its own); <see cref="UNSUPPORTED_NO_PROFILE"/> when
    /// the text shares no n-gram with any profile (a script no profile has), where "nearest"
    /// would be an arbitrary tie.
    /// </summary>
    /// <param name="guess">The identifier's unsupported verdict.</param>
    /// <returns>The key.</returns>
    public static string UnsupportedKey(LanguageGuess guess)
    {
        if (guess.DistancePpm >= LanguageIdentifier.PPM)
            return UNSUPPORTED_NO_PROFILE;

        return guess.PastLimit ? UNSUPPORTED_PAST_LIMIT + guess.Nearest : guess.Nearest;
    }

    /// <summary>Counts one document in the product.</summary>
    /// <param name="languageCode">Its language ("" when the stage is off).</param>
    public void Out(string languageCode)
    {
        DocumentsOut++;
        m_languages[languageCode] = m_languages.GetValueOrDefault(languageCode) + 1;
    }

    /// <summary>Counts one PDF the PDF stage took.</summary>
    /// <param name="truncated">Whether the crawler cut it short.</param>
    public void PdfIn(bool truncated)
    {
        m_pdf.In++;
        if (truncated)
            m_pdf.Truncated++;
    }

    /// <summary>Adds the fuel one PDF's run burned.</summary>
    /// <param name="fuel">The fuel.</param>
    public void PdfFuel(long fuel)
    {
        m_pdf.Fuel += Math.Max(0, fuel);
    }

    /// <summary>Counts one PDF that gave documents.</summary>
    /// <param name="pages">Its pages.</param>
    /// <param name="documents">The page-range documents it gave.</param>
    /// <param name="pagesCut">Its pages cut at the document cap.</param>
    /// <param name="runningLines">The lines removed from it as running headers and footers.</param>
    public void PdfExtracted(int pages, int documents, int pagesCut, int runningLines)
    {
        m_pdf.Extracted++;
        m_pdf.Pages += pages;
        m_pdf.Documents += documents;
        m_pdf.PagesCut += pagesCut;
        m_pdf.RunningLines += runningLines;
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
            UnsupportedLanguages = Sorted(m_unsupported),
            BytesFetched = bytesFetched,
            BytesProduced = bytesProduced,
            FetchMs = fetchMs,
            ProcessMs = processMs,
            WaitMs = waitMs,
            Pdf = m_pdf.In > 0 ? m_pdf.Clone() : null
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
