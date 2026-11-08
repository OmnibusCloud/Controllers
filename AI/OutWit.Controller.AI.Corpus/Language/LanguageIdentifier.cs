namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Identifies the language of a text against a <see cref="LanguageModel"/>: the text's own
/// ranked n-gram profile is compared with every language profile by the sum of rank
/// displacements (an n-gram the language lacks costs the maximum), the smallest distance
/// wins, and the margin to the runner-up is the confidence. Deterministic: integer
/// arithmetic, ordinal tie-breaks.
/// </summary>
public sealed class LanguageIdentifier
{
    #region Constants

    /// <summary>n-grams of the text taken into its profile.</summary>
    public const int TEXT_PROFILE_SIZE = 400;

    /// <summary>Characters of the text looked at: a document's head decides its language.</summary>
    public const int MAX_CHARS = 4000;

    /// <summary>Fewest letters a text must have for a verdict; below it the result is <see cref="LanguageGuess.Unknown"/>.</summary>
    public const int MIN_LETTERS = 20;

    #endregion

    #region Fields

    private readonly LanguageModel m_model;
    private readonly int m_maxRank;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an identifier over a model.
    /// </summary>
    /// <param name="model">The model.</param>
    public LanguageIdentifier(LanguageModel model)
    {
        m_model = model;
        m_maxRank = model.Profiles.Count == 0 ? 0 : model.Profiles.Max(profile => profile.Grams.Count);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Identifies the language of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The best language, the runner-up and the normalized margin between them.</returns>
    public LanguageGuess Identify(string text)
    {
        var counts = new Dictionary<ulong, long>();
        LanguageNgrams.CountPacked(text, counts, MAX_CHARS);

        long letters = 0;
        foreach (var (key, count) in counts)
        {
            if (LanguageNgrams.IsUnigram(key))
                letters += count;
        }

        if (letters < MIN_LETTERS || m_model.Profiles.Count == 0)
            return LanguageGuess.Unknown;

        var textProfile = LanguageNgrams.RankPacked(counts, TEXT_PROFILE_SIZE);

        string? best = null;
        string? second = null;
        long bestDistance = long.MaxValue;
        long secondDistance = long.MaxValue;
        foreach (var profile in m_model.Profiles)
        {
            long distance = 0;
            for (var i = 0; i < textProfile.Count; i++)
            {
                var rank = profile.RankOf(textProfile[i]);
                distance += rank < 0 ? m_maxRank : Math.Abs(rank - i);
            }

            if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(profile.Code, best) < 0))
            {
                second = best;
                secondDistance = bestDistance;
                best = profile.Code;
                bestDistance = distance;
            }
            else if (distance < secondDistance)
            {
                second = profile.Code;
                secondDistance = distance;
            }
        }

        var worst = (double)m_maxRank * textProfile.Count;
        var margin = second == null ? 1.0 : (secondDistance - bestDistance) / worst;
        return new LanguageGuess(best!, second, margin);
    }

    #endregion
}

/// <summary>The outcome of an identification.</summary>
/// <param name="Code">The best language code, or "" when the text is too short to tell.</param>
/// <param name="RunnerUp">The second-best code, or null.</param>
/// <param name="Margin">The distance margin to the runner-up as a share of the worst possible distance; larger is surer.</param>
public sealed record LanguageGuess(string Code, string? RunnerUp, double Margin)
{
    /// <summary>No verdict: too little text.</summary>
    public static readonly LanguageGuess Unknown = new("", null, 0);

    /// <summary>Whether a language was identified.</summary>
    public bool IsKnown => Code.Length > 0;
}
