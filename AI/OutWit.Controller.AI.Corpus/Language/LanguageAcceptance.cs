using System.Collections.Frozen;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The unsupported-language test of a language pack: which profiles a document may be labelled
/// with, and how close it must be. A pack holds profiles of the languages it supports and of
/// languages it knows but does not support (trained from the same labelled crawl text), so a
/// Swedish page finds its own profile instead of the German one. A document is labelled with its
/// nearest profile only when that profile is a supported language and the document lies inside
/// the language's acceptance region — its distance to the profile at most the language's
/// calibrated <see cref="MaxDistancePpm"/> — which catches the languages the pack has no profile
/// for at all. Anything else is unknown: a language the pack does not support. Integer
/// arithmetic: the same verdict on every node.
/// </summary>
public sealed class LanguageAcceptance
{
    #region Fields

    private readonly FrozenDictionary<string, int> m_maxDistancePpm;

    #endregion

    #region Constructors

    /// <summary>Creates the test.</summary>
    /// <param name="maxDistancePpm">Per supported language, the largest distance accepted, in millionths of the worst distance (0: no limit for that language).</param>
    /// <exception cref="ArgumentException">A limit is negative.</exception>
    public LanguageAcceptance(IReadOnlyDictionary<string, int> maxDistancePpm)
    {
        if (maxDistancePpm.Values.Any(value => value < 0 || value > LanguageIdentifier.PPM))
            throw new ArgumentException($"A distance limit is between 0 and {LanguageIdentifier.PPM}.", nameof(maxDistancePpm));

        m_maxDistancePpm = maxDistancePpm.ToFrozenDictionary(StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    /// <summary>Whether a document may be labelled with its nearest profile.</summary>
    /// <param name="code">The nearest profile's code.</param>
    /// <param name="distancePpm">The document's distance to it, in millionths of the worst distance.</param>
    /// <returns>True when the profile is a supported language and the distance is inside its region.</returns>
    public bool Accepts(string code, int distancePpm)
    {
        return m_maxDistancePpm.TryGetValue(code, out var max) && (max == 0 || distancePpm <= max);
    }

    /// <summary>Whether a code is a language the pack labels documents with.</summary>
    /// <param name="code">The code.</param>
    /// <returns>True for a supported language.</returns>
    public bool Supports(string code)
    {
        return m_maxDistancePpm.ContainsKey(code);
    }

    /// <summary>A supported language's distance limit.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The limit in millionths of the worst distance (0: none), or -1 for a code the pack does not support.</returns>
    public int MaxDistancePpm(string code)
    {
        return m_maxDistancePpm.TryGetValue(code, out var max) ? max : -1;
    }

    #endregion
}
