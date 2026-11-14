using System.Collections.Frozen;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// What the controller's code knows about scripts, the one piece of language knowledge that is
/// code rather than data: which scripts (ISO 15924 codes) are written without spaces between
/// words, so that the quality stages count their letters instead of whitespace-separated tokens
/// (<see cref="Quality.QualityFilter.IsUnspacedScript"/> holds their code points). Every other
/// script is taken as written with spaces, which needs no knowledge of the script at all. A
/// language pack names each language's script; a script written without spaces that is not in
/// this list needs a controller release.
/// </summary>
public static class LanguageScripts
{
    #region Constants

    /// <summary>The scripts written without spaces between words that the quality stages tokenize by letter: Han (and its simplified and traditional variants), Japanese, Thai, Lao, Khmer, Myanmar, Tibetan.</summary>
    public static readonly FrozenSet<string> UNSPACED = new[] { "Hani", "Hans", "Hant", "Jpan", "Thai", "Laoo", "Khmr", "Mymr", "Tibt" }.ToFrozenSet(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Whether a script is written without spaces between words.</summary>
    /// <param name="script">An ISO 15924 code (e.g. <c>Latn</c>, <c>Jpan</c>).</param>
    /// <returns>True for a script in <see cref="UNSPACED"/>.</returns>
    public static bool IsUnspaced(string script)
    {
        return UNSPACED.Contains(script);
    }

    /// <summary>Whether a string has the shape of an ISO 15924 code: four ASCII letters, the first upper case, the rest lower case.</summary>
    /// <param name="script">The candidate.</param>
    /// <returns>True for a well-formed code.</returns>
    public static bool IsWellFormed(string script)
    {
        return script.Length == 4
               && char.IsAsciiLetterUpper(script[0])
               && char.IsAsciiLetterLower(script[1])
               && char.IsAsciiLetterLower(script[2])
               && char.IsAsciiLetterLower(script[3]);
    }

    #endregion
}
