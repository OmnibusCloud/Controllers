namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// A document's words as ids, so its n-grams can be counted without building a string for
/// each: two positions hold the same n-gram exactly when their id sequences are equal — the same
/// equality as comparing the words joined by a space (a word never contains one). Every position
/// gets the class of its n-gram, numbered from 0: the classes of the n-grams are the ranks of the
/// pairs (class of the (n-1)-gram at the position, id of the n-th word), sorted with two counting
/// passes. No table is keyed by a hash of the words' ids: the words are the page's, and a hash
/// the page could choose its words against would put every n-gram in one chain. The words are
/// numbered by a table of the words themselves, whose string hashing the runtime draws per
/// process.
/// </summary>
internal sealed class NgramIndex
{
    #region Fields

    private readonly int[] m_ids;
    private readonly int[] m_lengths;
    private readonly int m_distinct;

    private int m_classesOf;
    private int[] m_classes;
    private int m_classCount;

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

        m_distinct = ids.Count;
        m_classesOf = 1;
        m_classes = m_ids;
        m_classCount = m_distinct;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The most frequent n-gram: the one with the most occurrences, overlapping ones included; on
    /// a tie, the one that occurs first (DataTrove's <c>find_top_duplicate</c>, a
    /// <c>Counter.most_common(1)</c> over the n-grams in text order).
    /// </summary>
    /// <param name="n">The n-gram length.</param>
    /// <returns>The position it first occurs at and its occurrence count; (-1, 0) when the document has fewer than n words.</returns>
    public (int Start, int Count) MostFrequent(int n)
    {
        if (n < 1 || m_ids.Length < n)
            return (-1, 0);

        var (classes, count) = ClassesOf(n);
        var occurrences = new int[count];
        var first = new int[count];
        Array.Fill(first, -1);
        for (var i = 0; i + n <= m_ids.Length; i++)
        {
            occurrences[classes[i]]++;
            if (first[classes[i]] < 0)
                first[classes[i]] = i;
        }

        var start = -1;
        var top = 0;
        for (var c = 0; c < count; c++)
        {
            if (occurrences[c] > top || (occurrences[c] == top && first[c] < start))
            {
                start = first[c];
                top = occurrences[c];
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
        if (n < 1 || m_ids.Length < n)
            return 0;

        var (classes, count) = ClassesOf(n);
        var seen = new bool[count];
        long chars = 0;
        var index = 0;
        while (index + n <= m_ids.Length)
        {
            if (seen[classes[index]])
            {
                chars += Chars(index, n);
                index += n;
            }
            else
            {
                seen[classes[index]] = true;
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

    #region Tools

    /// <summary>
    /// The class of the n-gram at every position where one starts, and how many classes there
    /// are. The filter asks for n = 2, 3, 4, then 5 to 10: each is made from the last one asked
    /// for, a smaller n from the words again.
    /// </summary>
    private (int[] Classes, int Count) ClassesOf(int n)
    {
        if (n < m_classesOf)
        {
            m_classesOf = 1;
            m_classes = m_ids;
            m_classCount = m_distinct;
        }

        while (m_classesOf < n)
        {
            (m_classes, m_classCount) = Extend(m_classes, m_classCount, m_classesOf);
            m_classesOf++;
        }

        return (m_classes, m_classCount);
    }

    /// <summary>
    /// The classes of the (length + 1)-grams from those of the length-grams: the pairs (class at
    /// i, id of the word at i + length) ranked by a counting sort on the word, then a stable one
    /// on the class; equal pairs, equal class.
    /// </summary>
    private (int[] Classes, int Count) Extend(int[] classes, int classCount, int length)
    {
        var positions = m_ids.Length - length;
        if (positions <= 0)
            return (Array.Empty<int>(), 0);

        // By the next word: a counting sort over the word ids.
        var byWord = new int[positions];
        var counts = new int[Math.Max(m_distinct, classCount) + 1];
        for (var i = 0; i < positions; i++)
            counts[m_ids[i + length] + 1]++;
        for (var v = 1; v <= m_distinct; v++)
            counts[v] += counts[v - 1];
        for (var i = 0; i < positions; i++)
            byWord[counts[m_ids[i + length]]++] = i;

        // Then by the class of what comes before it, keeping the order by word between equals.
        Array.Clear(counts);
        for (var i = 0; i < positions; i++)
            counts[classes[i] + 1]++;
        for (var v = 1; v <= classCount; v++)
            counts[v] += counts[v - 1];
        var sorted = new int[positions];
        foreach (var i in byWord)
            sorted[counts[classes[i]]++] = i;

        var next = new int[positions];
        var count = 0;
        for (var at = 0; at < positions; at++)
        {
            var i = sorted[at];
            if (at > 0)
            {
                var previous = sorted[at - 1];
                if (classes[previous] != classes[i] || m_ids[previous + length] != m_ids[i + length])
                    count++;
            }

            next[i] = count;
        }

        return (next, count + 1);
    }

    #endregion

    #region Properties

    /// <summary>Words in the document.</summary>
    public int Count => m_ids.Length;

    #endregion
}
