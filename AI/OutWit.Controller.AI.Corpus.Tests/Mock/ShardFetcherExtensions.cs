using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>A fetch whose task names exactly the host of its URL — the common shape of a test.</summary>
public static class ShardFetcherExtensions
{
    #region Functions

    /// <summary>Fetches a shard for a task that allows the URL's own host and nothing else.</summary>
    /// <param name="fetcher">The fetcher.</param>
    /// <param name="uri">The shard URL.</param>
    /// <param name="destinationPath">The file to write.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written.</returns>
    public static Task<long> FetchAsync(this ShardFetcher fetcher, Uri uri, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        return fetcher.FetchAsync(uri, [uri.IdnHost], destinationPath, maxBytes, cancellationToken);
    }

    #endregion
}
