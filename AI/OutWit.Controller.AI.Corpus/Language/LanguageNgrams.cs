using System.Globalization;
using System.Runtime.InteropServices;
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
    /// Counts the n-grams of a text exactly as <see cref="Count"/> does, each n-gram packed into
    /// a 64-bit key (<see cref="Pack(ReadOnlySpan{char})"/>) instead of a string: no allocation per
    /// n-gram. The identifier's path; the trainer keeps the string form.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="counts">The counts to add to.</param>
    /// <param name="maxChars">Characters of text to look at.</param>
    public static void CountPacked(string text, Dictionary<ulong, long> counts, int maxChars = int.MaxValue)
    {
        var word = new char[64];
        var length = 1;
        word[0] = BOUNDARY;
        Span<char> utf16 = stackalloc char[2];
        var seen = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen++ >= maxChars)
                break;

            int written;
            if (Rune.IsLetter(rune))
                written = Rune.ToLowerInvariant(rune).EncodeToUtf16(utf16);
            else if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                written = rune.EncodeToUtf16(utf16);
            else
            {
                length = FlushPacked(word, length, counts);
                continue;
            }

            if (length + written + 1 > word.Length)
                Array.Resize(ref word, word.Length * 2);
            utf16[..written].CopyTo(word.AsSpan(length));
            length += written;
        }

        FlushPacked(word, length, counts);
    }

    /// <summary>
    /// An n-gram of 1 to <see cref="MAX_N"/> UTF-16 characters as a 64-bit key: the characters
    /// big-endian, zero-padded. A zero never occurs inside a word, so the key is unambiguous, and
    /// comparing keys as numbers orders n-grams exactly as ordinal string comparison does.
    /// </summary>
    /// <param name="gram">The n-gram.</param>
    /// <returns>The key.</returns>
    public static ulong Pack(ReadOnlySpan<char> gram)
    {
        if (gram.Length is 0 or > MAX_N)
            throw new ArgumentException($"an n-gram has 1 to {MAX_N} characters", nameof(gram));

        ulong key = 0;
        for (var k = 0; k < MAX_N; k++)
            key = (key << 16) | (k < gram.Length ? gram[k] : 0u);
        return key;
    }

    /// <summary>Whether a packed key is a single character.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True for a 1-gram.</returns>
    public static bool IsUnigram(ulong key)
    {
        return (key & 0x0000_FFFF_FFFF_FFFFUL) == 0;
    }

    /// <summary>Orders packed n-grams as <see cref="Rank"/> orders strings (count descending, then ordinal) and keeps the top.</summary>
    /// <param name="counts">The counts.</param>
    /// <param name="top">How many to keep.</param>
    /// <returns>The ranked keys.</returns>
    public static List<ulong> RankPacked(Dictionary<ulong, long> counts, int top)
    {
        var entries = counts.ToArray();
        Array.Sort(entries, default(RankOrder));
        var ranked = new List<ulong>(Math.Min(top, entries.Length));
        for (var i = 0; i < entries.Length && i < top; i++)
            ranked.Add(entries[i].Key);
        return ranked;
    }

    private static int FlushPacked(char[] word, int length, Dictionary<ulong, long> counts)
    {
        if (length <= 1)
            return 1;

        word[length] = BOUNDARY;
        var padded = word.AsSpan(0, length + 1);
        for (var n = 1; n <= MAX_N; n++)
        {
            for (var i = 0; i + n <= padded.Length; i++)
            {
                var gram = padded.Slice(i, n);
                if (n == 1 && gram[0] == BOUNDARY)
                    continue;

                CollectionsMarshal.GetValueRefOrAddDefault(counts, Pack(gram), out _)++;
            }
        }

        return 1;
    }

    /// <summary>Count descending, then key ascending (ordinal n-gram order).</summary>
    private readonly struct RankOrder : IComparer<KeyValuePair<ulong, long>>
    {
        public int Compare(KeyValuePair<ulong, long> a, KeyValuePair<ulong, long> b)
        {
            return a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key);
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
