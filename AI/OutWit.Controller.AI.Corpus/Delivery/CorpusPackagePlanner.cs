namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// How the shards of a round are put into task packages. A round has at most
/// <see cref="MAX_PACKAGES"/> packages, whatever the manifest's length, so the collection the
/// platform assigns from and the collection it gathers the answers into are bounded: up to
/// <see cref="MAX_PACKAGES"/> shards each shard is a package of its own (the platform assigns and
/// samples shard by shard, as before packages), beyond that a package holds as many consecutive
/// shards as it takes to stay within the bound. A package is the unit the platform assigns by
/// the nodes' benchmark rates and runs twice when sampled; a shard is still the unit that is
/// fetched, processed, compared and reported.
/// </summary>
public static class CorpusPackagePlanner
{
    #region Constants

    /// <summary>The most packages a round has.</summary>
    public const int MAX_PACKAGES = 1024;

    /// <summary>The most shards a job has: a manifest that names more is refused by name.</summary>
    public const int MAX_SHARDS = 1 << 20;

    /// <summary>The most shards a package holds: <see cref="MAX_SHARDS"/> over <see cref="MAX_PACKAGES"/>. A node refuses a package that holds more, whoever made it.</summary>
    public const int MAX_PACKAGE_SHARDS = MAX_SHARDS / MAX_PACKAGES;

    #endregion

    #region Functions

    /// <summary>The shards a package of a round holds (its last package may hold fewer).</summary>
    /// <param name="shards">The round's shards.</param>
    /// <returns>1 up to <see cref="MAX_PACKAGES"/> shards, then the fewest that keep the round within <see cref="MAX_PACKAGES"/> packages.</returns>
    public static int ShardsPerPackage(int shards)
    {
        return shards <= MAX_PACKAGES ? 1 : (int)(((long)shards + MAX_PACKAGES - 1) / MAX_PACKAGES);
    }

    /// <summary>The packages a round of that many shards has.</summary>
    /// <param name="shards">The round's shards.</param>
    /// <returns>The package count.</returns>
    public static int Packages(int shards)
    {
        if (shards <= 0)
            return 0;

        var perPackage = ShardsPerPackage(shards);
        return (int)(((long)shards + perPackage - 1) / perPackage);
    }

    /// <summary>A value of every shard added up package by package, in the order the packages are made (consecutive shards).</summary>
    /// <param name="perShard">One value per shard, in shard order; a value of 0 or less is not counted.</param>
    /// <returns>One sum per package.</returns>
    public static List<long> PackageSums(IReadOnlyList<long> perShard)
    {
        var perPackage = ShardsPerPackage(perShard.Count);
        var sums = new List<long>(Packages(perShard.Count));

        for (var start = 0; start < perShard.Count; start += perPackage)
        {
            long sum = 0;
            var end = Math.Min(perShard.Count, start + perPackage);
            for (var index = start; index < end; index++)
                sum += Math.Max(0, perShard[index]);

            sums.Add(sum);
        }

        return sums;
    }

    #endregion
}
