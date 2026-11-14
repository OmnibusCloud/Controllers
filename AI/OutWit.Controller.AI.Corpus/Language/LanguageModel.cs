using System.Collections.Frozen;
using System.Security.Cryptography;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// A language model, in one of two formats with one canonical serialization each, so the same
/// content gives the same bytes and one SHA-256 pins the model in the pipeline contract:
/// <list type="bullet">
/// <item><b>WITLID1</b> — n-gram profiles only. Every profile is a language documents are
/// labelled with, the nearest one always wins, and the rule data is Corpus 0.1.14's
/// (<see cref="LanguageRuleSet.Witlid1"/>): what such a model did, it keeps doing.</item>
/// <item><b>WITLID2</b>, a language pack — everything a language needs, as data: the profiles of
/// the languages it supports with each one's rule data (script, stop words, thresholds) and
/// acceptance limit, and the profiles of languages it knows but does not support. A document
/// nearest to an unsupported profile, or too far from its nearest supported one, is unknown
/// (<see cref="LanguageAcceptance"/>). Adding a language is a new pack, not a new controller.</item>
/// </list>
/// The layouts are in <see cref="LanguageModelFormat"/>.
/// </summary>
public sealed class LanguageModel
{
    #region Constructors

    /// <summary>
    /// Creates a WITLID1 model from profiles (ordered by code for a stable serialization).
    /// </summary>
    /// <param name="profiles">The profiles, one per language code.</param>
    public LanguageModel(IEnumerable<LanguageProfile> profiles)
    {
        Format = LanguageModelFormat.WITLID1;
        Name = "";
        Profiles = profiles.OrderBy(profile => profile.Code, StringComparer.Ordinal).ToList();
        Languages = Profiles.Select(profile => profile.Code).ToList();
        Rules = LanguageRuleSet.Witlid1;
        Acceptance = null;
        Metadata = FrozenDictionary<string, string>.Empty;
    }

    /// <summary>Creates a WITLID2 language pack.</summary>
    /// <param name="name">The pack's name (e.g. <c>cc-2026-43-16</c>); part of its content.</param>
    /// <param name="profiles">Every profile: the supported languages' and the known unsupported ones'.</param>
    /// <param name="rules">The rule data of each supported language; a profile without rule data is a language the pack knows and does not support.</param>
    /// <param name="maxDistancePpm">Each supported language's acceptance limit in millionths of the worst distance (0, or absent: no limit).</param>
    /// <param name="metadata">What the pack says about itself (how it was built), for people; part of its content.</param>
    /// <exception cref="ArgumentException">The parts do not fit: rule data or a limit without a profile, a code twice, no supported language.</exception>
    public LanguageModel(string name, IEnumerable<LanguageProfile> profiles, LanguageRuleSet rules, IReadOnlyDictionary<string, int> maxDistancePpm, IReadOnlyDictionary<string, string> metadata)
    {
        Format = LanguageModelFormat.WITLID2;
        Name = name;
        Profiles = profiles.OrderBy(profile => profile.Code, StringComparer.Ordinal).ToList();

        var codes = Profiles.Select(profile => profile.Code).ToHashSet(StringComparer.Ordinal);
        if (codes.Count != Profiles.Count)
            throw new ArgumentException("A language has two profiles.", nameof(profiles));
        if (rules.Languages.Count == 0)
            throw new ArgumentException("A language pack supports at least one language.", nameof(rules));

        var stray = rules.Languages.Select(language => language.Code).Concat(maxDistancePpm.Keys).FirstOrDefault(code => !codes.Contains(code));
        if (stray != null)
            throw new ArgumentException($"'{stray}' has rule data or a limit but no profile.", nameof(rules));
        if (maxDistancePpm.Keys.Any(code => !rules.TryGet(code, out _)))
            throw new ArgumentException("A limit belongs to a supported language.", nameof(maxDistancePpm));

        Rules = rules;
        Languages = rules.Languages.Select(language => language.Code).ToList();
        Acceptance = new LanguageAcceptance(Languages.ToDictionary(code => code, code => maxDistancePpm.GetValueOrDefault(code), StringComparer.Ordinal));
        Metadata = metadata.ToFrozenDictionary(StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the model in its format.
    /// </summary>
    /// <returns>The model bytes.</returns>
    public byte[] ToBytes()
    {
        return LanguageModelFormat.Write(this);
    }

    /// <summary>
    /// Reads a model of either format.
    /// </summary>
    /// <param name="bytes">The model bytes.</param>
    /// <returns>The model.</returns>
    /// <exception cref="FormatException">The bytes are not a model.</exception>
    public static LanguageModel FromBytes(byte[] bytes)
    {
        return LanguageModelFormat.Read(bytes);
    }

    /// <summary>
    /// Loads a model file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The model.</returns>
    public static LanguageModel Load(string path)
    {
        return FromBytes(File.ReadAllBytes(path));
    }

    /// <summary>Lower-case hex SHA-256 of the serialized model: the pin the pipeline carries.</summary>
    /// <returns>The digest.</returns>
    public string Sha256()
    {
        return Convert.ToHexStringLower(SHA256.HashData(ToBytes()));
    }

    #endregion

    #region Properties

    /// <summary>The format: <see cref="LanguageModelFormat.WITLID1"/> or <see cref="LanguageModelFormat.WITLID2"/>.</summary>
    public string Format { get; }

    /// <summary>The pack's name ("" for a WITLID1 model).</summary>
    public string Name { get; }

    /// <summary>Every profile in code order (a pack's unsupported languages included).</summary>
    public IReadOnlyList<LanguageProfile> Profiles { get; }

    /// <summary>The languages a document may be labelled with, in code order: what <c>target_languages</c> may name.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>The rule data of the languages (Corpus 0.1.14's for a WITLID1 model).</summary>
    public LanguageRuleSet Rules { get; }

    /// <summary>The unsupported-language test, or null for a WITLID1 model (the nearest profile always wins).</summary>
    public LanguageAcceptance? Acceptance { get; }

    /// <summary>What the pack says about how it was built (empty for a WITLID1 model).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    #endregion
}
