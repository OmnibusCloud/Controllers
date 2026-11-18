namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The generator's own random numbers (SplitMix64): a few lines of integer arithmetic, so the
/// pages a seed gives are the same on every platform and under every runtime, for as long as the
/// committed shards have to be the generator's output. The runtime's seeded generator makes no
/// such promise.
/// </summary>
public sealed class CrawlSynthRandom
{
    #region Fields

    private ulong m_state;

    #endregion

    #region Constructors

    /// <summary>Starts a sequence.</summary>
    /// <param name="seed">The seed: the same seed, the same sequence.</param>
    public CrawlSynthRandom(ulong seed)
    {
        m_state = seed;
    }

    #endregion

    #region Functions

    /// <summary>The next 64 bits of the sequence.</summary>
    /// <returns>The value.</returns>
    public ulong NextUInt64()
    {
        unchecked
        {
            var z = m_state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A number below a bound.</summary>
    /// <param name="exclusiveMax">The bound, at least 1.</param>
    /// <returns>A number from 0 to the bound less one.</returns>
    public int Next(int exclusiveMax)
    {
        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    /// <summary>A number within a range, both ends included.</summary>
    /// <param name="min">The least value.</param>
    /// <param name="max">The greatest value.</param>
    /// <returns>The number.</returns>
    public int Between(int min, int max)
    {
        return min + Next(max - min + 1);
    }

    /// <summary>One item of a list.</summary>
    /// <param name="items">The list, not empty.</param>
    /// <returns>The item.</returns>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        return items[Next(items.Count)];
    }

    /// <summary>So many different items of a list, in the order they were drawn.</summary>
    /// <param name="items">The list.</param>
    /// <param name="count">How many; no more than the list holds are returned.</param>
    /// <returns>The items.</returns>
    public List<T> Draw<T>(IReadOnlyList<T> items, int count)
    {
        var pool = items.ToList();
        var drawn = new List<T>(Math.Min(count, pool.Count));
        while (drawn.Count < count && pool.Count > 0)
        {
            var index = Next(pool.Count);
            drawn.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return drawn;
    }

    /// <summary>Puts a list into a random order.</summary>
    /// <param name="items">The list.</param>
    public void Shuffle<T>(IList<T> items)
    {
        for (var last = items.Count - 1; last > 0; last--)
        {
            var other = Next(last + 1);
            (items[last], items[other]) = (items[other], items[last]);
        }
    }

    #endregion
}
