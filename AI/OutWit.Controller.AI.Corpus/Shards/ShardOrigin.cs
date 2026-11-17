namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The allowlist gate. Two questions are asked of every URL before a request leaves a
/// machine: does the task name this host (the submitter's list, which can only narrow), and
/// does this machine fetch from it at all (<see cref="ShardOriginPolicy"/>, which the
/// submitter cannot touch). A node checks both, whatever the host-side planner decided — the
/// task is the only authority a node has, and it is not enough on its own.
/// </summary>
public static class ShardOrigin
{
    #region Constants

    private const int MAX_SHOWN_CHARS = 128;

    #endregion

    #region Functions

    /// <summary>Parses a source URL and decides whether a task names it: an absolute HTTP(S) URL without credentials whose host is on the task's list.</summary>
    /// <param name="url">The shard URL.</param>
    /// <param name="allowedOrigins">Host names the task allows (exact, case-insensitive).</param>
    /// <param name="uri">The parsed URL when it passed, else null.</param>
    /// <returns>Null when the URL is one the task names, else the refusal.</returns>
    public static string? Check(string url, IReadOnlyCollection<string> allowedOrigins, out Uri? uri)
    {
        uri = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return $"'{url}' is not an absolute URL.";

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return $"'{url}': only http and https origins are fetched.";

        if (parsed.UserInfo.Length > 0)
            return $"'{parsed.Host}': a URL with a user name or password is not fetched.";

        // A host outside ASCII is mapped to its wire form by the machine's own library, and two
        // machines need not map it alike: it is written in its ASCII form or not fetched.
        if (!IsAsciiHost(url, parsed))
            return $"'{Shown(parsed.Host)}': a host that is not written in ASCII is not fetched; write it in its ASCII form (xn--...).";

        if (!IsAllowed(parsed, allowedOrigins))
            return $"'{parsed.Host}' is not an allowed origin.";

        uri = parsed;
        return null;
    }

    /// <summary>Decides whether this machine may send a request to a URL for a task: the task names it and the machine's policy permits it.</summary>
    /// <param name="url">The shard URL, or the target of a redirect.</param>
    /// <param name="allowedOrigins">Host names the task allows (exact, case-insensitive).</param>
    /// <param name="policy">What this machine fetches from.</param>
    /// <param name="uri">The parsed URL when the request may be made, else null.</param>
    /// <returns>Null when the request may be made, else the refusal.</returns>
    public static string? Check(string url, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, out Uri? uri)
    {
        var refusal = Check(url, allowedOrigins, out var parsed);
        if (refusal == null && parsed != null)
            refusal = policy.Check(parsed);

        uri = refusal == null ? parsed : null;
        return refusal;
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

    /// <summary>
    /// Whether a URL's host is written in ASCII: in the URL as given (no character outside ASCII
    /// and no percent escape before the path) and as parsed. Only such a host reads the same on
    /// every machine.
    /// </summary>
    /// <param name="url">The URL as the manifest or the task gives it.</param>
    /// <param name="parsed">The same URL, parsed.</param>
    /// <returns>True for an ASCII host.</returns>
    public static bool IsAsciiHost(string url, Uri parsed)
    {
        if (!IsAscii(parsed.Host))
            return false;

        var start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0)
            return false;

        for (var index = start + 3; index < url.Length; index++)
        {
            var c = url[index];
            if (c is '/' or '?' or '#' or '\\')
                break;

            if (c > 0x7F || c == '%')
                return false;
        }

        return true;
    }

    /// <summary>Whether a string holds ASCII characters only.</summary>
    /// <param name="text">The string.</param>
    /// <returns>True when no character is outside ASCII.</returns>
    public static bool IsAscii(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F)
                return false;
        }

        return true;
    }

    /// <summary>A host name as a message shows it: within a length, so one line of a manifest cannot make a message of any size.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The host, cut at 128 characters.</returns>
    public static string Shown(string host)
    {
        return host.Length <= MAX_SHOWN_CHARS ? host : host[..MAX_SHOWN_CHARS] + "...";
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
