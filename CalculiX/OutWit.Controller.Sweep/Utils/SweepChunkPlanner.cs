namespace OutWit.Controller.Sweep.Utils;

/// <summary>
/// Progressive chunk schedule of a sweep: small first (fast first feedback,
/// cheap smoke detection of a broken scenario), growing geometrically to a
/// cap that keeps the packing near-optimal while bounding both cancellation
/// loss and a failed node's recompute radius.
/// </summary>
public static class SweepChunkPlanner
{
    #region Constants

    /// <summary>Default first chunk size when the client passes none.</summary>
    public const int DEFAULT_FIRST_CHUNK_SIZE = 8;

    /// <summary>Default chunk size cap when the client passes none.</summary>
    public const int DEFAULT_MAX_CHUNK_SIZE = 48;

    private const int GROWTH_FACTOR = 2;

    #endregion

    #region Functions

    /// <summary>
    /// Computes the chunk sizes covering the whole variant table.
    /// </summary>
    /// <param name="firstChunkSize">First chunk size; 0 = default.</param>
    /// <param name="maxChunkSize">Chunk size cap; 0 = default.</param>
    /// <param name="totalVariants">Variant count of the study.</param>
    /// <returns>Chunk sizes, summing exactly to the variant count.</returns>
    public static List<int> Sizes(int firstChunkSize, int maxChunkSize, int totalVariants)
    {
        if (totalVariants <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalVariants), "A sweep needs at least one variant.");

        var first = firstChunkSize > 0 ? firstChunkSize : DEFAULT_FIRST_CHUNK_SIZE;
        var max = maxChunkSize > 0 ? maxChunkSize : DEFAULT_MAX_CHUNK_SIZE;

        if (max < first)
            max = first;

        var sizes = new List<int>();
        var remaining = totalVariants;
        var current = first;

        while (remaining > 0)
        {
            var size = System.Math.Min(current, remaining);
            sizes.Add(size);
            remaining -= size;
            current = System.Math.Min(current * GROWTH_FACTOR, max);
        }

        return sizes;
    }

    #endregion
}
