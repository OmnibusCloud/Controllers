using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Text to character n-grams, the same way for training and for identification: lower-cased
/// by the invariant culture, digits and punctuation become word boundaries, every word is
/// padded with the boundary mark so that word starts and ends count, and n-grams of 1 to
/// <see cref="MAX_N"/> characters are taken over the padded words. Pure functions of the
/// text — no culture, no locale, no platform in the result.
/// </summary>
public static class LanguageNgrams
{
    #region Constants

    /// <summary>Longest n-gram taken.</summary>
    public const int MAX_N = 4;

    /// <summary>The word boundary mark (not a letter in any script).</summary>
    public const char BOUNDARY = '_';

    #endregion

    #region Functions

    /// <summary>
    /// Counts the n-grams of a text into the dictionary (adding to existing counts).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="counts">The counts to add to.</param>
    /// <param name="maxChars">Characters of text to look at (the head of a long document is enough).</param>
    public static void Count(string text, Dictionary<string, long> counts, int maxChars = int.MaxValue)
    {
        var word = new StringBuilder(32);
        var seen = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen++ >= maxChars)
                break;

            if (Rune.IsLetter(rune))
            {
                word.Append(Rune.ToLowerInvariant(rune).ToString());
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                word.Append(rune.ToString());
                continue;
            }

            FlushWord(word, counts);
        }

        FlushWord(word, counts);
    }

    private static void FlushWord(StringBuilder word, Dictionary<string, long> counts)
    {
        if (word.Length == 0)
            return;

        var padded = BOUNDARY + word.ToString() + BOUNDARY;
        word.Clear();

        for (var n = 1; n <= MAX_N; n++)
        {
            for (var i = 0; i + n <= padded.Length; i++)
            {
                var gram = padded.Substring(i, n);
                if (gram.Length == 1 && gram[0] == BOUNDARY)
                    continue;

                counts[gram] = counts.GetValueOrDefault(gram) + 1;
            }
        }
    }

    /// <summary>
    /// Orders n-grams by count descending, ties by ordinal n-gram order, and keeps the top.
    /// </summary>
    /// <param name="counts">The counts.</param>
    /// <param name="top">How many to keep.</param>
    /// <returns>The ranked n-grams.</returns>
    public static List<string> Rank(Dictionary<string, long> counts, int top)
    {
        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(top)
            .Select(pair => pair.Key)
            .ToList();
    }

    #endregion
}
