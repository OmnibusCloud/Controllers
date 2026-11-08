using System.Text;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The quality filter over an extracted document: the Gopher-style shape rules (length, word
/// shape, symbols, bullets, ellipses) and repetition rules (duplicate lines, dominant and
/// repeated word n-grams), each a named rule with a measured value, so a shard's statistics
/// can say what dropped how much. Integer and rational arithmetic over the text; the same
/// verdict on every node.
/// </summary>
public static class QualityFilter
{
    #region Constants

    private const string RULE_MIN_WORDS = "min_words";
    private const string RULE_MAX_WORDS = "max_words";
    private const string RULE_MEAN_WORD_LENGTH = "mean_word_length";
    private const string RULE_SYMBOL_RATIO = "symbol_to_word_ratio";
    private const string RULE_BULLETS = "bullet_lines";
    private const string RULE_ELLIPSES = "ellipsis_lines";
    private const string RULE_ALPHABETIC = "alphabetic_words";
    private const string RULE_DUPLICATE_LINES = "duplicate_lines";
    private const string RULE_DUPLICATE_LINE_CHARS = "duplicate_line_chars";
    private const string RULE_TOP_NGRAM = "top_ngram_chars_";
    private const string RULE_DUPLICATE_NGRAM = "duplicate_ngram_chars_";

    private static readonly char[] BULLETS = ['•', '-', '*', '·', '●', '▪', '○'];

    #endregion

    #region Functions

    /// <summary>
    /// Judges a document.
    /// </summary>
    /// <param name="text">The extracted text (lines separated by '\n').</param>
    /// <param name="rules">The thresholds.</param>
    /// <returns>The verdict with every failed rule and the measurements behind it.</returns>
    public static QualityVerdict Judge(string text, QualityRules rules)
    {
        var failed = new List<string>();
        var measures = new Dictionary<string, double>(StringComparer.Ordinal);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var words = Words(text, rules);

        measures["words"] = words.Count;
        if (words.Count < rules.MinWords)
            failed.Add(RULE_MIN_WORDS);
        if (words.Count > rules.MaxWords)
            failed.Add(RULE_MAX_WORDS);

        if (words.Count > 0)
        {
            var meanLength = words.Average(w => (double)w.Length);
            measures["mean_word_length"] = meanLength;
            if (meanLength < rules.MinMeanWordLength || meanLength > rules.MaxMeanWordLength)
                failed.Add(RULE_MEAN_WORD_LENGTH);

            var symbols = text.Count(c => c == '#' || c == '…') + CountOccurrences(text, "...");
            measures["symbol_to_word_ratio"] = (double)symbols / words.Count;
            if (measures["symbol_to_word_ratio"] > rules.MaxSymbolToWordRatio)
                failed.Add(RULE_SYMBOL_RATIO);

            var alphabetic = words.Count(w => w.Any(char.IsLetter));
            measures["alphabetic_words"] = (double)alphabetic / words.Count;
            if (measures["alphabetic_words"] < rules.MinAlphabeticWordRatio)
                failed.Add(RULE_ALPHABETIC);
        }

        if (lines.Length > 0)
        {
            var bullets = lines.Count(line => line.TrimStart() is { Length: > 0 } trimmed && BULLETS.Contains(trimmed[0]));
            measures["bullet_lines"] = (double)bullets / lines.Length;
            if (measures["bullet_lines"] > rules.MaxBulletLineRatio)
                failed.Add(RULE_BULLETS);

            var ellipses = lines.Count(line => line.TrimEnd().EndsWith("...", StringComparison.Ordinal) || line.TrimEnd().EndsWith('…'));
            measures["ellipsis_lines"] = (double)ellipses / lines.Length;
            if (measures["ellipsis_lines"] > rules.MaxEllipsisLineRatio)
                failed.Add(RULE_ELLIPSES);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicateLines = 0;
            var duplicateChars = 0;
            var totalChars = 0;
            foreach (var line in lines)
            {
                totalChars += line.Length;
                if (!seen.Add(line))
                {
                    duplicateLines++;
                    duplicateChars += line.Length;
                }
            }

            measures["duplicate_lines"] = (double)duplicateLines / lines.Length;
            measures["duplicate_line_chars"] = totalChars == 0 ? 0 : (double)duplicateChars / totalChars;
            if (measures["duplicate_lines"] > rules.MaxDuplicateLineRatio)
                failed.Add(RULE_DUPLICATE_LINES);
            if (measures["duplicate_line_chars"] > rules.MaxDuplicateLineCharRatio)
                failed.Add(RULE_DUPLICATE_LINE_CHARS);
        }

        var wordChars = words.Sum(w => w.Length);
        if (wordChars > 0)
        {
            for (var n = 2; n <= 4; n++)
            {
                var ratio = TopNgramCharRatio(words, n, wordChars);
                measures[RULE_TOP_NGRAM + n] = ratio;
                if (ratio > rules.MaxTopNgramCharRatio[n - 2])
                    failed.Add(RULE_TOP_NGRAM + n);
            }

            for (var n = 5; n <= 10; n++)
            {
                var ratio = DuplicateNgramCharRatio(words, n, wordChars);
                measures[RULE_DUPLICATE_NGRAM + n] = ratio;
                if (ratio > rules.MaxDuplicateNgramCharRatio[n - 5])
                    failed.Add(RULE_DUPLICATE_NGRAM + n);
            }
        }

        return new QualityVerdict(failed.Count == 0, failed, measures);
    }

    /// <summary>
    /// The words of a text under the rules: space-separated tokens, or, for an unspaced
    /// script, the letters themselves.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="rules">The rules (their shape decides the split).</param>
    /// <returns>The words.</returns>
    public static List<string> Words(string text, QualityRules rules)
    {
        if (ReferenceEquals(rules, QualityRules.Unspaced) || rules.MinMeanWordLength == 0 && rules.MinAlphabeticWordRatio == 0)
        {
            var letters = new List<string>();
            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.IsLetter(rune))
                    letters.Add(rune.ToString());
            }

            return letters;
        }

        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static double TopNgramCharRatio(List<string> words, int n, int wordChars)
    {
        if (words.Count < n)
            return 0;

        var counts = new Dictionary<string, (int Count, int Chars)>(StringComparer.Ordinal);
        for (var i = 0; i + n <= words.Count; i++)
        {
            var key = string.Join(' ', words.GetRange(i, n));
            var chars = key.Length - (n - 1);
            counts[key] = counts.TryGetValue(key, out var entry) ? (entry.Count + 1, entry.Chars) : (1, chars);
        }

        var top = counts.Values.MaxBy(entry => (long)entry.Count * entry.Chars);
        return (double)top.Count * top.Chars / wordChars;
    }

    private static double DuplicateNgramCharRatio(List<string> words, int n, int wordChars)
    {
        if (words.Count < n)
            return 0;

        // Characters covered by every occurrence of an n-gram that occurs more than once, each
        // word counted once even when several repeated n-grams cover it.
        var occurrences = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i + n <= words.Count; i++)
        {
            var key = string.Join(' ', words.GetRange(i, n));
            if (!occurrences.TryGetValue(key, out var list))
                occurrences[key] = list = [];
            list.Add(i);
        }

        var covered = new bool[words.Count];
        foreach (var list in occurrences.Values.Where(list => list.Count > 1))
        {
            foreach (var start in list)
            {
                for (var j = start; j < start + n; j++)
                    covered[j] = true;
            }
        }

        var chars = 0;
        for (var i = 0; i < words.Count; i++)
        {
            if (covered[i])
                chars += words[i].Length;
        }

        return (double)chars / wordChars;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    #endregion
}

/// <summary>The outcome of the quality filter on one document.</summary>
/// <param name="Passed">Whether every rule held.</param>
/// <param name="FailedRules">The rules that did not, by name.</param>
/// <param name="Measures">The measured values by rule name (and "words").</param>
public sealed record QualityVerdict(bool Passed, IReadOnlyList<string> FailedRules, IReadOnlyDictionary<string, double> Measures);
