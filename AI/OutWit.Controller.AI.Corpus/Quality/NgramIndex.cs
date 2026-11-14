using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// A document's words as ids, so its n-grams can be counted without building a string for
/// each: an n-gram is named by the position it starts at, and two positions name the same
/// n-gram exactly when their id sequences are equal — the same equality as comparing the
/// words joined by a space (a word never contains one). One dictionary of counts per n, keyed
/// by the first position of each distinct n-gram, answers for any position.
/// </summary>
internal sealed class NgramIndex
{
    #region Fields

    private readonly int[] m_ids;
    private readonly int[] m_lengths;

    #endregion

    #region Constructors

    /// <summary>Numbers the words of a document.</summary>
    /// <param name="words">The words, in order.</param>
    public NgramIndex(IReadOnlyList<string> words)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        m_ids = new int[words.Count];
        m_lengths = new int[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            if (!ids.TryGetValue(words[i], out var id))
                ids[words[i]] = id = ids.Count;
            m_ids[i] = id;
            m_lengths[i] = words[i].Length;
        }
    }

    #endregion

    #region Functions

    /// <summary>The occurrence count of every n-gram, answerable by any position it starts at.</summary>
    /// <param name="n">The n-gram length.</param>
    /// <returns>Counts keyed by start position under sequence equality.</returns>
    public Dictionary<int, int> Occurrences(int n)
    {
        var counts = new Dictionary<int, int>(Math.Max(0, m_ids.Length - n + 1), new SequenceComparer(m_ids, n));
        for (var i = 0; i + n <= m_ids.Length; i++)
            CollectionsMarshal.GetValueRefOrAddDefault(counts, i, out _)++;

        return counts;
    }

    /// <summary>
    /// The most frequent n-gram: the one with the most occurrences, overlapping ones included; on
    /// a tie, the one that occurs first (DataTrove's <c>find_top_duplicate</c>, a
    /// <c>Counter.most_common(1)</c> over the n-grams in text order).
    /// </summary>
    /// <param name="n">The n-gram length.</param>
    /// <returns>The position it first occurs at and its occurrence count; (-1, 0) when the document has fewer than n words.</returns>
    public (int Start, int Count) MostFrequent(int n)
    {
        var start = -1;
        var top = 0;
        foreach (var (first, count) in Occurrences(n))
        {
            if (count > top || (count == top && first < start))
            {
                start = first;
                top = count;
            }
        }

        return (start, top);
    }

    /// <summary>
    /// Characters of the words of every n-gram occurrence that repeats an n-gram seen earlier,
    /// scanning the words in order: a repeat is counted and the scan moves past its n words, any
    /// other n-gram is remembered and the scan moves one word (DataTrove's
    /// <c>find_all_duplicate</c>). No word is counted twice, and the first occurrence of an
    /// n-gram is never counted.
    /// </summary>
    /// <param name="n">The n-gram length.</param>
    /// <returns>The characters of the repeats' words (separators not included).</returns>
    public long RepeatedChars(int n)
    {
        var seen = new HashSet<int>(Math.Max(0, m_ids.Length - n + 1), new SequenceComparer(m_ids, n));
        long chars = 0;
        var index = 0;
        while (index + n <= m_ids.Length)
        {
            if (seen.Contains(index))
            {
                chars += Chars(index, n);
                index += n;
            }
            else
            {
                seen.Add(index);
                index++;
            }
        }

        return chars;
    }

    /// <summary>Characters of the words of the n-gram at a position.</summary>
    /// <param name="start">The position.</param>
    /// <param name="n">The n-gram length.</param>
    /// <returns>The sum of its word lengths.</returns>
    public int Chars(int start, int n)
    {
        var chars = 0;
        for (var i = start; i < start + n; i++)
            chars += m_lengths[i];
        return chars;
    }

    #endregion

    #region Properties

    /// <summary>Words in the document.</summary>
    public int Count => m_ids.Length;

    #endregion

    #region Types

    /// <summary>Two start positions are equal when the n ids from each are equal.</summary>
    private sealed class SequenceComparer(int[] ids, int n) : IEqualityComparer<int>
    {
        public bool Equals(int first, int second)
        {
            return first == second || ids.AsSpan(first, n).SequenceEqual(ids.AsSpan(second, n));
        }

        public int GetHashCode(int start)
        {
            var hash = 17;
            for (var i = start; i < start + n; i++)
                hash = unchecked(hash * 31 + ids[i]);
            return hash;
        }
    }

    #endregion
}
