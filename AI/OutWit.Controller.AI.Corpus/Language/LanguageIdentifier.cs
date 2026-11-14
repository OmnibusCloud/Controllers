namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Identifies the language of a text against a <see cref="LanguageModel"/>: the text's own
/// ranked n-gram profile is compared with every language profile by the sum of rank
/// displacements (an n-gram the language lacks costs the maximum), the smallest distance
/// wins, and the margin to the runner-up is the confidence. With a language pack the winner
/// must also pass the pack's unsupported-language test (<see cref="LanguageAcceptance"/>):
/// nearest to a profile the pack knows but does not support, or outside the winner's
/// acceptance region, the text is in a language the pack does not support. Deterministic:
/// integer arithmetic, ordinal tie-breaks.
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

    /// <summary>The unit of a distance: millionths of the worst distance a text of its profile size can have.</summary>
    public const int PPM = 1_000_000;

    #endregion

    #region Fields

    private readonly LanguageModel m_model;
    private readonly LanguageAcceptance? m_acceptance;
    private readonly int m_maxRank;
    private readonly Dictionary<ulong, (int Profile, int Rank)[]> m_index;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an identifier over a model, with the model's unsupported-language test.
    /// </summary>
    /// <param name="model">The model.</param>
    public LanguageIdentifier(LanguageModel model)
        : this(model, model.Acceptance)
    {
    }

    /// <summary>
    /// Creates an identifier over a model with a test of the caller's (null: the nearest profile
    /// always wins, as a WITLID1 model has it — what a pack's calibration measures).
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="acceptance">The unsupported-language test, or null.</param>
    public LanguageIdentifier(LanguageModel model, LanguageAcceptance? acceptance)
    {
        m_model = model;
        m_acceptance = acceptance;
        m_maxRank = model.Profiles.Count == 0 ? 0 : model.Profiles.Max(profile => profile.Grams.Count);
        m_index = Index(model);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Identifies the language of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The best language, the runner-up and the normalized margin between them; unknown when the text is too short or in a language the pack does not support.</returns>
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
        var distances = Distances(textProfile);

        string? best = null;
        string? second = null;
        long bestDistance = long.MaxValue;
        long secondDistance = long.MaxValue;
        for (var p = 0; p < distances.Length; p++)
        {
            var profile = m_model.Profiles[p];
            var distance = distances[p];
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
        var distancePpm = Ppm(bestDistance, (long)m_maxRank * textProfile.Count);

        if (m_acceptance != null && !m_acceptance.Accepts(best!, distancePpm))
            return new LanguageGuess("", second, margin) { Nearest = best!, DistancePpm = distancePpm, IsUnsupported = true, PastLimit = m_acceptance.Supports(best!) };

        return new LanguageGuess(best!, second, margin) { Nearest = best!, DistancePpm = distancePpm };
    }

    /// <summary>
    /// The distance of a text to every profile of the model, the way <see cref="Identify"/>
    /// measures them, for tools that study the model (calibration, evaluation).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="worst">The worst distance a text of this profile size can have (the unit of <see cref="Ppm"/>).</param>
    /// <returns>Per profile, in the model's profile order, the distance; null when the text is too short to tell.</returns>
    public long[]? Measure(string text, out long worst)
    {
        worst = 0;
        var counts = new Dictionary<ulong, long>();
        LanguageNgrams.CountPacked(text, counts, MAX_CHARS);

        long letters = 0;
        foreach (var (key, count) in counts)
        {
            if (LanguageNgrams.IsUnigram(key))
                letters += count;
        }

        if (letters < MIN_LETTERS || m_model.Profiles.Count == 0)
            return null;

        var textProfile = LanguageNgrams.RankPacked(counts, TEXT_PROFILE_SIZE);
        worst = (long)m_maxRank * textProfile.Count;
        return Distances(textProfile);
    }

    /// <summary>
    /// The out-of-place distance of a text profile to every language profile: per text n-gram,
    /// its displacement in each profile that has it, the maximum in each that does not. Summed
    /// through an inverted index — one lookup per text n-gram, then the profiles holding it —
    /// as the maximum for every n-gram, corrected where a profile has it: the same integers as a
    /// sum per profile, whatever the number of profiles.
    /// </summary>
    private long[] Distances(List<ulong> textProfile)
    {
        var distances = new long[m_model.Profiles.Count];
        Array.Fill(distances, (long)m_maxRank * textProfile.Count);
        for (var i = 0; i < textProfile.Count; i++)
        {
            if (!m_index.TryGetValue(textProfile[i], out var postings))
                continue;

            foreach (var (profile, rank) in postings)
                distances[profile] += Math.Abs(rank - i) - m_maxRank;
        }

        return distances;
    }

    /// <summary>Every profile's n-grams by packed key: the profiles (in model order) that hold it and its rank there, the first rank when a profile lists it twice (<see cref="LanguageProfile.RankOf(ulong)"/>).</summary>
    private static Dictionary<ulong, (int Profile, int Rank)[]> Index(LanguageModel model)
    {
        var lists = new Dictionary<ulong, List<(int Profile, int Rank)>>();
        for (var p = 0; p < model.Profiles.Count; p++)
        {
            var grams = model.Profiles[p].Grams;
            for (var rank = 0; rank < grams.Count; rank++)
            {
                if (grams[rank].Length is 0 or > LanguageNgrams.MAX_N)
                    continue;

                var key = LanguageNgrams.Pack(grams[rank]);
                if (!lists.TryGetValue(key, out var list))
                {
                    list = [];
                    lists[key] = list;
                }

                if (list.Count == 0 || list[^1].Profile != p)
                    list.Add((p, rank));
            }
        }

        return lists.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    /// <summary>A distance in millionths of the worst one, rounded down (integer arithmetic).</summary>
    /// <param name="distance">The distance.</param>
    /// <param name="worst">The worst distance a text of its profile size can have.</param>
    /// <returns>The distance in <see cref="PPM"/> units.</returns>
    public static int Ppm(long distance, long worst)
    {
        return worst <= 0 ? 0 : (int)(distance * PPM / worst);
    }

    #endregion
}

/// <summary>The outcome of an identification.</summary>
/// <param name="Code">The language code, or "" when the text is too short to tell or in a language the model does not support.</param>
/// <param name="RunnerUp">The second-best code, or null.</param>
/// <param name="Margin">The distance margin to the runner-up as a share of the worst possible distance; larger is surer.</param>
public sealed record LanguageGuess(string Code, string? RunnerUp, double Margin)
{
    /// <summary>No verdict: too little text.</summary>
    public static readonly LanguageGuess Unknown = new("", null, 0);

    /// <summary>Whether a language was identified.</summary>
    public bool IsKnown => Code.Length > 0;

    /// <summary>Whether the text has enough letters but is in a language the model does not support (its pack's test refused the nearest profile).</summary>
    public bool IsUnsupported { get; init; }

    /// <summary>The nearest profile's code, whether or not it was accepted ("" when the text is too short).</summary>
    public string Nearest { get; init; } = "";

    /// <summary>The distance to the nearest profile in millionths of the worst distance (0 when the text is too short).</summary>
    public int DistancePpm { get; init; }

    /// <summary>Whether an unsupported verdict came from the limit: the nearest profile is a supported language, the text lies past its limit (false when the nearest profile is a language the pack knows and does not support).</summary>
    public bool PastLimit { get; init; }
}
