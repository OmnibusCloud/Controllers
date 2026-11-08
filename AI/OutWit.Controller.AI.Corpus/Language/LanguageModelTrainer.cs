using System.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Builds a <see cref="LanguageModel"/> from labelled text: Common Crawl's WET records carry
/// the language its classifier identified (<c>WARC-Identified-Content-Language</c>), so a
/// handful of public WET files is a labelled corpus of hundreds of thousands of documents in
/// a hundred languages. Documents labelled with one language only are used (a mixed page
/// teaches nothing clean); languages with too few documents are left out. The training is a
/// deterministic function of the input files in order.
/// </summary>
public sealed class LanguageModelTrainer
{
    #region Constants

    /// <summary>n-grams kept per language.</summary>
    public const int DEFAULT_PROFILE_SIZE = 3000;

    /// <summary>Fewest documents a language needs to get a profile.</summary>
    public const int DEFAULT_MIN_DOCUMENTS = 200;

    /// <summary>Characters taken from each document (its head).</summary>
    public const int DOCUMENT_CHARS = 4000;

    #endregion

    #region Fields

    private readonly int m_profileSize;
    private readonly int m_minDocuments;
    private readonly Dictionary<string, Dictionary<string, long>> m_counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> m_documents = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a trainer.
    /// </summary>
    /// <param name="profileSize">n-grams kept per language.</param>
    /// <param name="minDocuments">Fewest documents a language needs.</param>
    public LanguageModelTrainer(int profileSize = DEFAULT_PROFILE_SIZE, int minDocuments = DEFAULT_MIN_DOCUMENTS)
    {
        m_profileSize = profileSize;
        m_minDocuments = minDocuments;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Adds one labelled document.
    /// </summary>
    /// <param name="code">The language code.</param>
    /// <param name="text">The document text.</param>
    public void Add(string code, string text)
    {
        if (!m_counts.TryGetValue(code, out var counts))
        {
            counts = new Dictionary<string, long>(StringComparer.Ordinal);
            m_counts[code] = counts;
        }

        LanguageNgrams.Count(text, counts, DOCUMENT_CHARS);
        m_documents[code] = m_documents.GetValueOrDefault(code) + 1;
    }

    /// <summary>
    /// Adds every single-language conversion record of a WET file.
    /// </summary>
    /// <param name="wetPath">The WET file.</param>
    /// <returns>Documents added.</returns>
    public int AddWet(string wetPath)
    {
        var added = 0;
        using var reader = WarcReader.Open(wetPath);
        foreach (var record in reader.ReadRecords())
        {
            if (record.Type != "conversion")
                continue;

            var language = record.IdentifiedLanguage;
            if (string.IsNullOrEmpty(language) || language.Contains(','))
                continue;

            Add(language, Encoding.UTF8.GetString(record.Payload.Span));
            added++;
        }

        return added;
    }

    /// <summary>
    /// Builds the model from what was added.
    /// </summary>
    /// <returns>The model.</returns>
    public LanguageModel Build()
    {
        var profiles = m_counts
            .Where(pair => m_documents[pair.Key] >= m_minDocuments)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new LanguageProfile(pair.Key, LanguageNgrams.Rank(pair.Value, m_profileSize)));

        return new LanguageModel(profiles);
    }

    #endregion

    #region Properties

    /// <summary>Documents seen per language code.</summary>
    public IReadOnlyDictionary<string, int> Documents => m_documents;

    #endregion
}
