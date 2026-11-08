namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The allowlist gate: a node fetches only from the hosts its task names, over HTTP(S). The
/// host checks it before any request, whatever the host-side planner decided — the task is
/// the only authority a node has.
/// </summary>
public static class ShardOrigin
{
    #region Functions

    /// <summary>Parses a source URL and decides whether a task may fetch it.</summary>
    /// <param name="url">The shard URL.</param>
    /// <param name="allowedOrigins">Host names the task allows (exact, case-insensitive).</param>
    /// <param name="uri">The parsed URL when it is absolute and HTTP(S), else null.</param>
    /// <returns>Null when the fetch may proceed, else the refusal.</returns>
    public static string? Check(string url, IReadOnlyCollection<string> allowedOrigins, out Uri? uri)
    {
        uri = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return $"'{url}' is not an absolute URL.";

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return $"'{url}': only http and https origins are fetched.";

        if (!IsAllowed(parsed, allowedOrigins))
            return $"'{parsed.Host}' is not an allowed origin.";

        uri = parsed;
        return null;
    }

    /// <summary>Whether a URL's host is on the allowlist (exact, case-insensitive; ports and paths do not matter).</summary>
    /// <param name="uri">The URL.</param>
    /// <param name="allowedOrigins">Host names the task allows.</param>
    /// <returns>True when the host is allowed.</returns>
    public static bool IsAllowed(Uri uri, IReadOnlyCollection<string> allowedOrigins)
    {
        foreach (var origin in allowedOrigins)
        {
            if (string.Equals(Normalize(origin), uri.IdnHost, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>The host part of an allowlist entry: a bare host stays, a URL or host:port loses the rest.</summary>
    /// <param name="origin">The allowlist entry.</param>
    /// <returns>The host name, lower-case.</returns>
    public static string Normalize(string origin)
    {
        var trimmed = origin.Trim();
        if (trimmed.Length == 0)
            return "";

        if (Uri.TryCreate(trimmed.Contains("://") ? trimmed : $"http://{trimmed}", UriKind.Absolute, out var uri))
            return uri.IdnHost.ToLowerInvariant();

        return trimmed.ToLowerInvariant();
    }

    #endregion
}
