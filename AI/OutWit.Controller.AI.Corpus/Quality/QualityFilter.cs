using System.Text;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The quality filter over an extracted document: the Gopher quality rules (length, word
/// shape, symbols, bullets, ellipses, alphabetic words; Rae et al. 2021, appendix A.1.1) and
/// its repetition rules (duplicate lines, the most frequent word n-gram, repeated word
/// n-grams; table A1), each a named rule with a measured value, so a shard's statistics can
/// say what dropped how much. Where the paper leaves a measure open, it is computed the way
/// DataTrove's <c>GopherQualityFilter</c> and <c>GopherRepetitionFilter</c> compute it (the code
/// behind FineWeb); the one deliberate difference is the word: a whitespace-separated token
/// (as in Dolma's Gopher tagger) or, for a script without word spacing, a letter, where
/// DataTrove runs a language's word tokenizer. Integer and rational arithmetic over the text;
/// the same verdict on every node.
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
    private const string RULE_STOP_WORDS = "stop_words";
    private const string RULE_LOREM_IPSUM = "lorem_ipsum";

    private const string LOREM_IPSUM = "lorem ipsum";

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

            // "A symbol-to-word ratio greater than 0.1 for either the hash symbol or the ellipsis"
            // (Gopher): two ratios, each against the threshold, as DataTrove checks them; the
            // measure is the larger.
            var hashes = text.Count(c => c == '#');
            var ellipses = text.Count(c => c == '…') + CountOccurrences(text, "...");
            measures["symbol_to_word_ratio"] = (double)Math.Max(hashes, ellipses) / words.Count;
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

            var ellipsisLines = lines.Count(line => line.TrimEnd().EndsWith("...", StringComparison.Ordinal) || line.TrimEnd().EndsWith('…'));
            measures["ellipsis_lines"] = (double)ellipsisLines / lines.Length;
            if (measures["ellipsis_lines"] > rules.MaxEllipsisLineRatio)
                failed.Add(RULE_ELLIPSES);

            // A duplicate line is one that repeats an earlier line; its characters are counted
            // against the whole text (DataTrove's find_duplicates over the lines, / len(text)).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicateLines = 0;
            long duplicateChars = 0;
            foreach (var line in lines)
            {
                if (!seen.Add(line))
                {
                    duplicateLines++;
                    duplicateChars += line.Length;
                }
            }

            measures["duplicate_lines"] = (double)duplicateLines / lines.Length;
            measures["duplicate_line_chars"] = (double)duplicateChars / text.Length;
            if (measures["duplicate_lines"] > rules.MaxDuplicateLineRatio)
                failed.Add(RULE_DUPLICATE_LINES);
            if (measures["duplicate_line_chars"] > rules.MaxDuplicateLineCharRatio)
                failed.Add(RULE_DUPLICATE_LINE_CHARS);
        }

        if (words.Count > 0)
        {
            var ngrams = new NgramIndex(words);
            var separator = rules.CountsLetters ? 0 : 1;
            for (var n = 2; n <= 4; n++)
            {
                var ratio = TopNgramCharRatio(ngrams, n, separator, text.Length);
                measures[RULE_TOP_NGRAM + n] = ratio;
                if (ratio > rules.MaxTopNgramCharRatio[n - 2])
                    failed.Add(RULE_TOP_NGRAM + n);
            }

            // "The fraction of characters contained within all duplicate n-grams, taking care not
            // to count characters that occur in overlapping n-grams more than once" (Gopher): a
            // duplicate is an occurrence that repeats an earlier one — the first occurrence is
            // the original, as with lines — counted by its words' characters over the text
            // (DataTrove's find_all_duplicate, / len(text)). Counting the first occurrence too
            // (Corpus 0.1.13 and earlier, as Dolma's tagger does) doubles the measure of a phrase
            // said twice: on four Common Crawl WET files the 5-gram rule then failed 51 % of the
            // pages (28 % after the line stage), counting repeats only 25 % (13 %).
            for (var n = 5; n <= 10; n++)
            {
                var ratio = (double)ngrams.RepeatedChars(n) / text.Length;
                measures[RULE_DUPLICATE_NGRAM + n] = ratio;
                if (ratio > rules.MaxDuplicateNgramCharRatio[n - 5])
                    failed.Add(RULE_DUPLICATE_NGRAM + n);
            }
        }

        // Gopher's stop-word filter, "at least two of the following English words", which "deals
        // with ostensibly English documents that contain no coherent English text"; FineWeb-2
        // gives each language a list of its own (a language pack's rule data). On Common Crawl it mostly
        // removes pages in a language the identifier does not know, labelled with the nearest
        // one it does. Distinct words, matched exactly (DataTrove: stop_words ∩ set(words)).
        if (rules.MinStopWords > 0)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in words)
            {
                if (rules.StopWords.Contains(word))
                    found.Add(word);
            }

            measures["stop_words"] = found.Count;
            if (found.Count < rules.MinStopWords)
                failed.Add(RULE_STOP_WORDS);
        }

        // C4: "we removed any page where the phrase 'lorem ipsum' appeared" (also in FineWeb's
        // C4 filter): a template or a demo page.
        if (text.Contains(LOREM_IPSUM, StringComparison.OrdinalIgnoreCase))
            failed.Add(RULE_LOREM_IPSUM);

        return new QualityVerdict(failed.Count == 0, failed, measures);
    }

    /// <summary>
    /// The words of a text under the rules: space-separated tokens, or, for a script without
    /// word spacing, each of its letters — with one exception: a run of letters of a spaced
    /// script inside such a text (a Latin product name in a Japanese page) is one word, as it is
    /// in the text it was borrowed from. Digits and punctuation are not words there.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="rules">The rules (their shape decides the split).</param>
    /// <returns>The words.</returns>
    public static List<string> Words(string text, QualityRules rules)
    {
        if (!rules.CountsLetters)
            return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        var words = new List<string>();
        var run = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) && !IsUnspacedScript(rune.Value))
            {
                run.Append(rune.ToString());
                continue;
            }

            if (run.Length > 0)
            {
                words.Add(run.ToString());
                run.Clear();
            }

            if (Rune.IsLetter(rune))
                words.Add(rune.ToString());
        }

        if (run.Length > 0)
            words.Add(run.ToString());

        return words;
    }

    /// <summary>Whether a code point belongs to a script written without spaces between words (Han, kana, Bopomofo, Thai, Lao, Tibetan, Myanmar, Khmer).</summary>
    /// <param name="value">The code point.</param>
    /// <returns>True for such a script.</returns>
    public static bool IsUnspacedScript(int value)
    {
        return value is >= 0x0E00 and <= 0x0EFF       // Thai, Lao
            or >= 0x0F00 and <= 0x0FFF                // Tibetan
            or >= 0x1000 and <= 0x109F                // Myanmar
            or >= 0x1780 and <= 0x17FF                // Khmer
            or >= 0x2E80 and <= 0x2FDF                // CJK radicals, Kangxi radicals
            or >= 0x3005 and <= 0x3007                // ideographic iteration and closing marks, ideographic zero
            or >= 0x3021 and <= 0x3029                // Hangzhou numerals
            or >= 0x3031 and <= 0x3035                // kana repeat marks
            or >= 0x3040 and <= 0x30FF                // Hiragana, Katakana
            or >= 0x3100 and <= 0x31BF                // Bopomofo
            or >= 0x31F0 and <= 0x31FF                // Katakana phonetic extensions
            or >= 0x3400 and <= 0x4DBF                // CJK extension A
            or >= 0x4E00 and <= 0x9FFF                // CJK unified ideographs
            or >= 0xF900 and <= 0xFAFF                // CJK compatibility ideographs
            or >= 0xFF66 and <= 0xFF9F                // half-width Katakana
            or >= 0x20000 and <= 0x323AF;             // CJK extensions B .. H
    }

    private static double TopNgramCharRatio(NgramIndex ngrams, int n, int separator, int textLength)
    {
        if (ngrams.Count < n)
            return 0;

        // "The fraction of characters contained within the most frequently-occurring n-gram"
        // (Gopher): the n-gram with the most occurrences, its characters as written — the words
        // and the separators between them — times its count, over the text (DataTrove's
        // find_top_duplicate over " ".join n-grams, / len(text)).
        var (start, count) = ngrams.MostFrequent(n);
        var chars = ngrams.Chars(start, n) + (long)separator * (n - 1);
        return (double)(chars * count) / textLength;
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
