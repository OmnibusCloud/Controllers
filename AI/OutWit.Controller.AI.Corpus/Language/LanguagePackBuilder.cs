using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Trains the profiles of a language pack from Common Crawl WET files, whose records carry the
/// language the crawl's classifier identified: the supported languages (those with source data,
/// <see cref="LanguagePackSource"/>) from the training files, the same way
/// <see cref="LanguageModelTrainer"/> trains a WITLID1 model — the same files give the same
/// profiles — and every other language with enough documents, from the training files and the
/// optional known-language files, as a language the pack knows and does not support. The result
/// has no distance limits yet; <see cref="LanguagePackCalibrator"/> sets them. A deterministic
/// function of the input files and the source data.
/// </summary>
public sealed class LanguagePackBuilder
{
    #region Constants

    /// <summary>Fewest documents a language needs to become a known (unsupported) profile.</summary>
    public const int DEFAULT_KNOWN_MIN_DOCUMENTS = 100;

    private const string RECORD_CONVERSION = "conversion";

    #endregion

    #region Fields

    private readonly LanguageRuleSet m_supported;
    private readonly int m_profileSize;
    private readonly int m_minDocuments;
    private readonly int m_knownMinDocuments;
    private readonly LanguageModelTrainer m_trainer;
    private readonly LanguageModelTrainer m_knownTrainer;
    private readonly List<string> m_training = [];
    private readonly List<string> m_knownTraining = [];

    #endregion

    #region Constructors

    /// <summary>Creates a builder.</summary>
    /// <param name="supported">The supported languages' rule data.</param>
    /// <param name="profileSize">n-grams kept per profile.</param>
    /// <param name="minDocuments">Fewest training documents a supported language needs.</param>
    /// <param name="knownMinDocuments">Fewest documents another language needs to become a known profile.</param>
    public LanguagePackBuilder(LanguageRuleSet supported, int profileSize = LanguageModelTrainer.DEFAULT_PROFILE_SIZE,
        int minDocuments = LanguageModelTrainer.DEFAULT_MIN_DOCUMENTS, int knownMinDocuments = DEFAULT_KNOWN_MIN_DOCUMENTS)
    {
        m_supported = supported;
        m_profileSize = profileSize;
        m_minDocuments = minDocuments;
        m_knownMinDocuments = knownMinDocuments;
        m_trainer = new LanguageModelTrainer(profileSize, 1);
        m_knownTrainer = new LanguageModelTrainer(profileSize, knownMinDocuments);
    }

    #endregion

    #region Functions

    /// <summary>Adds a training file: its supported languages' documents train their profiles, the others' the known profiles.</summary>
    /// <param name="wetPath">The WET file.</param>
    /// <returns>Single-language documents added.</returns>
    public int AddTrainingWet(string wetPath)
    {
        m_training.Add(Describe(wetPath));
        return Add(wetPath, knownOnly: false);
    }

    /// <summary>Adds a file for the known profiles only: rare languages need more text than the supported ones' training files hold.</summary>
    /// <param name="wetPath">The WET file.</param>
    /// <returns>Documents of unsupported languages added.</returns>
    public int AddKnownTrainingWet(string wetPath)
    {
        m_knownTraining.Add(Describe(wetPath));
        return Add(wetPath, knownOnly: true);
    }

    /// <summary>Builds the pack without distance limits.</summary>
    /// <param name="name">The pack's name.</param>
    /// <returns>The pack.</returns>
    /// <exception cref="InvalidOperationException">A supported language has too few training documents.</exception>
    public LanguageModel Build(string name)
    {
        var supported = m_trainer.Build().Profiles.ToDictionary(profile => profile.Code, StringComparer.Ordinal);
        var missing = m_supported.Languages
            .Where(language => m_trainer.Documents.GetValueOrDefault(language.Code) < m_minDocuments)
            .Select(language => $"{language.Code} has {m_trainer.Documents.GetValueOrDefault(language.Code)}")
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Too few training documents (each supported language needs {m_minDocuments}): {string.Join(", ", missing)}.");

        var known = m_knownTrainer.Build().Profiles.Where(profile => !m_supported.TryGet(profile.Code, out _)).ToList();
        var profiles = m_supported.Languages.Select(language => supported[language.Code]).Concat(known);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = $"character n-gram profiles (n = 1 to {LanguageNgrams.MAX_N}, {m_profileSize} per language), out-of-place distance (Cavnar-Trenkle); unknown when the nearest profile is a known unsupported language or the distance is past the language's limit",
            ["built_by"] = $"corpus-assets build-language-pack, Corpus {ControllerBuildInfo.VERSION}",
            ["training"] = string.Join("; ", m_training),
            ["training_documents"] = Counts(m_supported.Languages.Select(language => language.Code), m_trainer.Documents),
            ["min_documents"] = m_minDocuments.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["known_min_documents"] = m_knownMinDocuments.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["known_documents"] = Counts(known.Select(profile => profile.Code), m_knownTrainer.Documents)
        };
        if (m_knownTraining.Count > 0)
            metadata["known_training"] = string.Join("; ", m_knownTraining);

        return new LanguageModel(name, profiles, m_supported, new Dictionary<string, int>(), metadata);
    }

    /// <summary>A file as the pack's metadata names it: its name and its SHA-256 (not its path, which is the builder's machine's).</summary>
    /// <param name="path">The file.</param>
    /// <returns>"name sha256:hex".</returns>
    public static string Describe(string path)
    {
        using var stream = File.OpenRead(path);
        return $"{Path.GetFileName(path)} sha256:{Convert.ToHexStringLower(SHA256.HashData(stream))}";
    }

    private int Add(string wetPath, bool knownOnly)
    {
        var added = 0;
        using var reader = WarcReader.Open(wetPath);
        foreach (var record in reader.ReadRecords())
        {
            // The records LanguageModelTrainer.AddWet takes, the text it takes from them.
            if (record.Type != RECORD_CONVERSION)
                continue;

            var language = record.IdentifiedLanguage;
            if (string.IsNullOrEmpty(language) || language.Contains(','))
                continue;

            var supported = m_supported.TryGet(language, out _);
            if (supported && knownOnly)
                continue;

            (supported ? m_trainer : m_knownTrainer).Add(language, Encoding.UTF8.GetString(record.Payload.Span));
            added++;
        }

        return added;
    }

    private static string Counts(IEnumerable<string> codes, IReadOnlyDictionary<string, int> documents)
    {
        return string.Join(" ", codes.Order(StringComparer.Ordinal).Select(code => $"{code}={documents.GetValueOrDefault(code)}"));
    }

    #endregion

    #region Properties

    /// <summary>Training documents per supported language.</summary>
    public IReadOnlyDictionary<string, int> Documents => m_trainer.Documents;

    /// <summary>Documents per other language, over the training and known-language files.</summary>
    public IReadOnlyDictionary<string, int> KnownDocuments => m_knownTrainer.Documents;

    #endregion
}
