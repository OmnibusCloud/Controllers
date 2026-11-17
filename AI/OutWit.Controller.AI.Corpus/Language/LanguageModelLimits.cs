using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The most a language model may hold, sized at several times what a real pack needs (the
/// reference pack: 0.6 MB, 39 profiles of 3,000 n-grams, 117,000 n-grams in all, up to 22 stop
/// words a language, a metadata value of 1,300 characters). A model is a lab's input that every
/// node of a job loads and keeps: its size on disk, its counts and the memory it takes once read
/// (about 64 bytes an n-gram) are bounded here, and nothing is allocated by a number the file
/// states. A model past a limit is refused by name on the server, at Split and at Preflight.
/// </summary>
public static class LanguageModelLimits
{
    #region Constants

    /// <summary>The most bytes a model file may have.</summary>
    public const int MAX_BYTES = 16 * 1024 * 1024;

    /// <summary>The most profiles a model may hold: every document costs a distance per profile.</summary>
    public const int MAX_PROFILES = 512;

    /// <summary>The most n-grams one profile may list.</summary>
    public const int MAX_GRAMS = 16_384;

    /// <summary>The most n-grams every profile of a model may list together: what bounds the memory a loaded model takes (about 32 MB at the limit).</summary>
    public const int MAX_TOTAL_GRAMS = 524_288;

    /// <summary>The most stop words one language may list.</summary>
    public const int MAX_STOP_WORDS = 4096;

    /// <summary>The most characters a stop word may have.</summary>
    public const int MAX_STOP_WORD_CHARS = 64;

    /// <summary>The most entries a pack's metadata may have.</summary>
    public const int MAX_METADATA = 64;

    /// <summary>The most characters a metadata key, a metadata value or a pack's name may have.</summary>
    public const int MAX_TEXT_CHARS = 8192;

    /// <summary>The most thresholds one language may override.</summary>
    public const int MAX_OVERRIDES = 64;

    /// <summary>The most values one threshold override may carry.</summary>
    public const int MAX_OVERRIDE_VALUES = 16;

    /// <summary>The most characters a language code may have.</summary>
    public const int MAX_CODE_CHARS = 16;

    /// <summary>The characters a language code may have beside ASCII letters and digits.</summary>
    public const string CODE_MARKS = "-_";

    /// <summary>Models a process keeps loaded at once; the least recently used one goes first.</summary>
    public const int MAX_CACHED = 4;

    #endregion

    #region Functions

    /// <summary>Whether a string may be a language code: 1 to <see cref="MAX_CODE_CHARS"/> ASCII letters, digits, hyphens and underscores.</summary>
    /// <param name="code">The code; may be null.</param>
    /// <returns>True when it may.</returns>
    public static bool IsCode(string? code)
    {
        return CorpusText.IsName(code, MAX_CODE_CHARS, CODE_MARKS);
    }

    #endregion
}
