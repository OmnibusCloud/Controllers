using System.Text;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The boilerplate-line stage: a page's navigation, prices, counters and footers come out as
/// short lines without a sentence's ending, the article as sentences and long lines. A line
/// stays when it reads like prose — it ends a sentence and has a few words, or it is long —
/// and goes otherwise; a line about JavaScript, cookies or the site's policies goes too. These
/// are the C4 line rules (Raffel et al. 2020, section 2.2; TensorFlow Datasets'
/// <c>c4_utils.clean_page</c>), made script-aware — for scripts without word spacing the
/// measure is letters — and keeping one kind of line C4 drops: a long line without a final
/// mark. The quality filter then judges the text that is left, and that text is the product, so
/// a page whose article survives its menu is kept and is clean.
/// </summary>
public static class LineCleaner
{
    #region Constants

    /// <summary>Fewest words of a line that ends a sentence (spaced scripts). C4: "we only retained lines that contained at least 5 words".</summary>
    public const int MIN_WORDS_TERMINATED = 5;

    /// <summary>
    /// Fewest words of a line that does not (a wrapped paragraph, a list item, a long heading).
    /// Ours: C4 drops every such line, FineWeb keeps every line of three words; read on Common
    /// Crawl text, about half of the unterminated lines of twelve words and more are prose, and
    /// they carry about a sixth of an English WET page's characters.
    /// </summary>
    public const int MIN_WORDS_UNTERMINATED = 12;

    /// <summary>Fewest letters of a line that ends a sentence (scripts without word spacing; ours, a few words' worth).</summary>
    public const int MIN_LETTERS_TERMINATED = 6;

    /// <summary>Fewest letters of a line that does not (ours).</summary>
    public const int MIN_LETTERS_UNTERMINATED = 40;

    /// <summary>A line with a longer word goes (C4's <c>_MAX_WORD_LENGTH</c>: an encoded blob, not text).</summary>
    public const int MAX_WORD_LENGTH = 1000;

    private const string TERMINALS = ".!?。．！？‼⁇؟۔।॥";
    private const string CLOSERS = "\"'”’»)]」』）】〕›";

    // C4: "we removed any line with the word Javascript" — the notices asking to enable it.
    private const string JAVASCRIPT = "javascript";

    // C4's _POLICY_SUBSTRINGS (also FineWeb's C4 filter): consent and policy boilerplate.
    private static readonly string[] POLICY = ["terms of use", "privacy policy", "cookie policy", "uses cookies", "use of cookies", "use cookies"];

    // Ours: C4's phrases catch about half of the cookie banners and consent tables in Common
    // Crawl text, in English only. A line that names cookies at all is such a line in about 98 %
    // of the English cases (the rest are recipes) and in practically all others, whatever the
    // language, since the web's word is borrowed.
    private const string COOKIE = "cookie";

    #endregion

    #region Functions

    /// <summary>Removes the lines that do not read like prose.</summary>
    /// <param name="text">The document's text, one block per line.</param>
    /// <param name="rules">The document's quality rules (they say whether words or letters count).</param>
    /// <param name="removed">How many lines went.</param>
    /// <returns>The kept lines, joined by '\n' (empty when none is kept).</returns>
    public static string Clean(string text, QualityRules rules, out int removed)
    {
        var builder = new StringBuilder(text.Length);
        removed = 0;

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;

            if (!Keep(line, rules))
            {
                removed++;
                continue;
            }

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(line);
        }

        return builder.ToString();
    }

    /// <summary>Whether a line reads like prose under a document's rules.</summary>
    /// <param name="line">One line, whitespace already collapsed.</param>
    /// <param name="rules">The document's quality rules.</param>
    /// <returns>True to keep the line.</returns>
    public static bool Keep(string line, QualityRules rules)
    {
        return IsProseShaped(line, rules) && !IsBoilerplate(line);
    }

    /// <summary>Whether a line is long enough for its ending: a sentence of a few words, or a long line.</summary>
    /// <param name="line">One line, whitespace already collapsed.</param>
    /// <param name="rules">The document's quality rules.</param>
    /// <returns>True when the line has the shape of prose.</returns>
    public static bool IsProseShaped(string line, QualityRules rules)
    {
        var terminated = EndsSentence(line);
        if (rules.CountsLetters)
        {
            var letters = 0;
            foreach (var rune in line.EnumerateRunes())
            {
                if (Rune.IsLetter(rune))
                    letters++;
            }

            return letters >= (terminated ? MIN_LETTERS_TERMINATED : MIN_LETTERS_UNTERMINATED);
        }

        var words = 0;
        var inWord = false;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
                inWord = false;
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words >= (terminated ? MIN_WORDS_TERMINATED : MIN_WORDS_UNTERMINATED);
    }

    /// <summary>
    /// Whether a line is page furniture whatever its shape: it mentions JavaScript, cookies or
    /// the site's terms or privacy policy (compared without case), or holds a word longer than
    /// <see cref="MAX_WORD_LENGTH"/>.
    /// </summary>
    /// <param name="line">One line.</param>
    /// <returns>True when the line goes.</returns>
    public static bool IsBoilerplate(string line)
    {
        if (line.Contains(JAVASCRIPT, StringComparison.OrdinalIgnoreCase) || line.Contains(COOKIE, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var phrase in POLICY)
        {
            if (line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var length = 0;
        foreach (var c in line)
        {
            length = char.IsWhiteSpace(c) ? 0 : length + 1;
            if (length > MAX_WORD_LENGTH)
                return true;
        }

        return false;
    }

    /// <summary>Whether a line ends a sentence: its last character, past closing quotes and brackets, is a terminal mark — not an ellipsis, the ending of a teaser.</summary>
    /// <param name="line">The line.</param>
    /// <returns>True when the line ends a sentence.</returns>
    public static bool EndsSentence(string line)
    {
        var end = line.Length - 1;
        while (end >= 0 && (char.IsWhiteSpace(line[end]) || CLOSERS.Contains(line[end])))
            end--;

        if (end < 0 || !TERMINALS.Contains(line[end]))
            return false;

        if (line[end] == '.' && end >= 2 && line[end - 1] == '.' && line[end - 2] == '.')
            return false;

        return line[end] != '…';
    }

    #endregion
}
