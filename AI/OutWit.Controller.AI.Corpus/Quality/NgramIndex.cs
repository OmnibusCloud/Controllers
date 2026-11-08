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

    /// <summary>The length of the word at a position.</summary>
    /// <param name="index">The position.</param>
    /// <returns>Its character count.</returns>
    public int Length(int index)
    {
        return m_lengths[index];
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
