namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// What a language pack did with the documents of one crawl label (<see cref="LanguagePackEvaluator"/>):
/// how many it labelled with which language, how many it called unsupported or too short, and
/// the same documents without the unsupported test — labelled with their nearest supported
/// language, as a WITLID1 model labels them — including those the quality stages would keep.
/// </summary>
public sealed class LanguagePackTally
{
    #region Fields

    private readonly SortedDictionary<string, int> m_labelled = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> m_nearest = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Counts one document.</summary>
    /// <param name="guess">The pack's verdict.</param>
    /// <param name="nearestSupported">The nearest supported language ("" when the text is too short).</param>
    /// <param name="kept">Whether the quality stages keep it under its nearest supported language (false when not judged).</param>
    /// <param name="keptChars">The characters they keep.</param>
    public void Count(LanguageGuess guess, string nearestSupported, bool kept, long keptChars)
    {
        Documents++;
        if (nearestSupported.Length == 0)
        {
            TooShort++;
            return;
        }

        m_nearest[nearestSupported] = m_nearest.GetValueOrDefault(nearestSupported) + 1;
        if (guess.IsKnown)
            m_labelled[guess.Code] = m_labelled.GetValueOrDefault(guess.Code) + 1;
        else
            Unsupported++;

        if (!kept)
            return;

        Kept++;
        KeptChars += keptChars;
        if (guess.IsKnown)
            return;

        KeptUnsupported++;
        KeptCharsUnsupported += keptChars;
    }

    /// <summary>Adds another tally's counts (the same label counted elsewhere, e.g. in another file).</summary>
    /// <param name="other">The other tally.</param>
    public void Add(LanguagePackTally other)
    {
        Documents += other.Documents;
        TooShort += other.TooShort;
        Unsupported += other.Unsupported;
        Kept += other.Kept;
        KeptUnsupported += other.KeptUnsupported;
        KeptChars += other.KeptChars;
        KeptCharsUnsupported += other.KeptCharsUnsupported;
        foreach (var (code, count) in other.m_labelled)
            m_labelled[code] = m_labelled.GetValueOrDefault(code) + count;
        foreach (var (code, count) in other.m_nearest)
            m_nearest[code] = m_nearest.GetValueOrDefault(code) + count;
    }

    /// <summary>Documents the pack labelled with a language.</summary>
    /// <param name="code">The language.</param>
    /// <returns>The count.</returns>
    public int LabelledAs(string code)
    {
        return m_labelled.GetValueOrDefault(code);
    }

    /// <summary>Documents whose nearest supported language is a language (what a test-less model labels them).</summary>
    /// <param name="code">The language.</param>
    /// <returns>The count.</returns>
    public int NearestIs(string code)
    {
        return m_nearest.GetValueOrDefault(code);
    }

    #endregion

    #region Properties

    /// <summary>Documents of the label.</summary>
    public int Documents { get; private set; }

    /// <summary>Documents too short to identify (unknown either way).</summary>
    public int TooShort { get; private set; }

    /// <summary>Documents the pack calls a language it does not support.</summary>
    public int Unsupported { get; private set; }

    /// <summary>Documents the quality stages keep under their nearest supported language.</summary>
    public int Kept { get; private set; }

    /// <summary>Of those, the ones the pack calls unsupported.</summary>
    public int KeptUnsupported { get; private set; }

    /// <summary>The characters the quality stages keep of <see cref="Kept"/>.</summary>
    public long KeptChars { get; private set; }

    /// <summary>The characters of <see cref="KeptUnsupported"/>.</summary>
    public long KeptCharsUnsupported { get; private set; }

    /// <summary>The pack's labels with their counts, in code order.</summary>
    public IReadOnlyDictionary<string, int> Labelled => m_labelled;

    /// <summary>The nearest supported languages with their counts, in code order.</summary>
    public IReadOnlyDictionary<string, int> Nearest => m_nearest;

    #endregion
}
