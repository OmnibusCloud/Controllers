using System.Text;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The boilerplate-line stage: a page's navigation, prices, counters and footers come out as
/// short lines without a sentence's ending, the article as sentences and long lines. A line
/// stays when it reads like prose — it ends a sentence and has a few words, or it is long —
/// and goes otherwise (the C4 line rule, made script-aware: for scripts without word spacing
/// the measure is letters). The quality filter then judges the text that is left, and that
/// text is the product, so a page whose article survives its menu is kept and is clean.
/// </summary>
public static class LineCleaner
{
    #region Constants

    /// <summary>Fewest words of a line that ends a sentence (spaced scripts).</summary>
    public const int MIN_WORDS_TERMINATED = 3;

    /// <summary>Fewest words of a line that does not (a wrapped paragraph, a long heading).</summary>
    public const int MIN_WORDS_UNTERMINATED = 12;

    /// <summary>Fewest letters of a line that ends a sentence (scripts without word spacing).</summary>
    public const int MIN_LETTERS_TERMINATED = 6;

    /// <summary>Fewest letters of a line that does not.</summary>
    public const int MIN_LETTERS_UNTERMINATED = 40;

    private const string TERMINALS = ".!?。．！？‼⁇؟۔।॥";
    private const string CLOSERS = "\"'”’»)]」』）】〕›";

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
