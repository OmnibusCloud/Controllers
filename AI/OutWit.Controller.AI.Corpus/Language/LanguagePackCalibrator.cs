using System.Globalization;
using System.Text;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Sets a language pack's distance limits from labelled text the pack was not trained on. For
/// every document Common Crawl labelled with one supported language that the pack's profiles
/// also put nearest to that language, its distance to the profile is a sample of the language's
/// acceptance region; a language's limit is the sample at the target recall (0.999: the region
/// holds 999 of 1000 such documents). Documents are read the way the pipeline reads a WET
/// record. Integer arithmetic over sorted samples: the same files give the same limits.
/// </summary>
public sealed class LanguagePackCalibrator
{
    #region Constants

    /// <summary>The target recall of the default pack, in millionths: its region holds 999 of 1000 of a language's calibration documents.</summary>
    public const int DEFAULT_RECALL_PPM = 999_000;

    /// <summary>Fewest calibration documents a supported language needs for a limit.</summary>
    public const int MIN_DOCUMENTS = 50;

    private const string RECORD_CONVERSION = "conversion";

    #endregion

    #region Fields

    private readonly LanguageModel m_pack;
    private readonly LanguageIdentifier m_identifier;
    private readonly Dictionary<string, List<int>> m_samples = new(StringComparer.Ordinal);
    private readonly List<string> m_files = [];

    #endregion

    #region Constructors

    /// <summary>Creates a calibrator for a pack (its limits, if any, are ignored and replaced).</summary>
    /// <param name="pack">A WITLID2 pack.</param>
    /// <exception cref="ArgumentException">The model is not a pack.</exception>
    public LanguagePackCalibrator(LanguageModel pack)
    {
        if (pack.Format != LanguageModelFormat.WITLID2)
            throw new ArgumentException("Only a language pack has limits to calibrate.", nameof(pack));

        m_pack = pack;
        m_identifier = new LanguageIdentifier(pack, new LanguageAcceptance(pack.Languages.ToDictionary(code => code, _ => 0, StringComparer.Ordinal)));
        foreach (var code in pack.Languages)
            m_samples[code] = [];
    }

    #endregion

    #region Functions

    /// <summary>Adds one labelled document.</summary>
    /// <param name="label">The language the document is labelled with.</param>
    /// <param name="text">The text as the pipeline reads it.</param>
    /// <returns>True when it became a sample.</returns>
    public bool Add(string label, string text)
    {
        if (!m_samples.TryGetValue(label, out var samples))
            return false;

        var guess = m_identifier.Identify(text);
        if (guess.Code != label)
            return false;

        samples.Add(guess.DistancePpm);
        return true;
    }

    /// <summary>Adds the single-language documents of a WET file.</summary>
    /// <param name="wetPath">The WET file.</param>
    /// <returns>Samples added.</returns>
    public int AddWet(string wetPath)
    {
        m_files.Add(LanguagePackBuilder.Describe(wetPath));

        var added = 0;
        using var reader = WarcReader.Open(wetPath);
        foreach (var record in reader.ReadRecords())
        {
            var label = record.IdentifiedLanguage;
            if (record.Type != RECORD_CONVERSION || string.IsNullOrEmpty(label) || label.Contains(',') || !m_samples.ContainsKey(label))
                continue;

            var text = HtmlTextExtractor.CollapseLines(Encoding.UTF8.GetString(record.Payload.Span));
            if (Add(label, text))
                added++;
        }

        return added;
    }

    /// <summary>Adds another calibrator's samples (the same pack over other files), after this one's.</summary>
    /// <param name="other">The other calibrator.</param>
    public void Add(LanguagePackCalibrator other)
    {
        m_files.AddRange(other.m_files);
        foreach (var (code, samples) in other.m_samples)
        {
            if (m_samples.TryGetValue(code, out var mine))
                mine.AddRange(samples);
        }
    }

    /// <summary>The limits at a target recall.</summary>
    /// <param name="recallPpm">The share of a language's samples its region holds, in millionths (1 to 1,000,000).</param>
    /// <returns>Per supported language, the limit in <see cref="LanguageIdentifier.PPM"/> units.</returns>
    /// <exception cref="InvalidOperationException">A language has fewer than <see cref="MIN_DOCUMENTS"/> samples.</exception>
    public IReadOnlyDictionary<string, int> Limits(int recallPpm)
    {
        if (recallPpm is <= 0 or > LanguageIdentifier.PPM)
            throw new ArgumentOutOfRangeException(nameof(recallPpm), "The recall is above 0 and at most 1.");

        var missing = m_samples.Where(pair => pair.Value.Count < MIN_DOCUMENTS).Select(pair => $"{pair.Key} has {pair.Value.Count}").ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Too few calibration documents (each supported language needs {MIN_DOCUMENTS}): {string.Join(", ", missing)}.");

        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, samples) in m_samples)
        {
            var sorted = samples.Order().ToList();
            var rank = (int)(((long)recallPpm * sorted.Count + LanguageIdentifier.PPM - 1) / LanguageIdentifier.PPM);
            limits[code] = sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
        }

        return limits;
    }

    /// <summary>The pack with the limits at a target recall, the calibration recorded in its metadata.</summary>
    /// <param name="recallPpm">The target recall in millionths.</param>
    /// <returns>The calibrated pack.</returns>
    public LanguageModel Apply(int recallPpm)
    {
        var limits = Limits(recallPpm);
        var metadata = new Dictionary<string, string>(m_pack.Metadata, StringComparer.Ordinal)
        {
            ["calibration"] = string.Join("; ", m_files),
            ["calibration_documents"] = string.Join(" ", m_samples.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value.Count}")),
            ["recall"] = (recallPpm / (double)LanguageIdentifier.PPM).ToString("0.######", CultureInfo.InvariantCulture)
        };

        return new LanguageModel(m_pack.Name, m_pack.Profiles, m_pack.Rules, limits, metadata);
    }

    #endregion

    #region Properties

    /// <summary>Samples per supported language.</summary>
    public IReadOnlyDictionary<string, int> Documents => m_samples.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);

    #endregion
}
