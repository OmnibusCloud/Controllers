using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Real crawl data for the tests that must see it, without a byte of it in the repository: the
/// committed list <c>Fixtures/crawl-heads.json</c> names a file of Common Crawl, a byte range
/// and the SHA-256 of that range, and nothing of the content. The range is fetched from
/// <c>data.commoncrawl.org</c> when a test first asks for it, checked against the hash, and kept
/// in the test project's <c>@Data/crawl</c> (gitignored) for the next run; a copy that is there
/// and has the listed hash is not fetched again. The folder can be named otherwise by
/// <c>WITAI_CORPUS_CRAWL_DIR</c>.
/// <para>
/// A test that needs a head on a machine that cannot reach the origin is ignored, with the
/// reason; on the build server it fails, so that a green run has read the real data. A range
/// that arrives and does not have its hash fails everywhere: that is a pin that moved, not a
/// network that is away.
/// </para>
/// </summary>
public static class CorpusCrawlHeads
{
    #region Constants

    /// <summary>The committed list: names, ranges, hashes.</summary>
    public const string LISTING = "crawl-heads.json";

    /// <summary>Names another folder to keep the fetched heads in.</summary>
    public const string FOLDER_VARIABLE = "WITAI_CORPUS_CRAWL_DIR";

    private const string CI_ENV = "GITHUB_ACTIONS";
    private const string PROJECT_FILE = "OutWit.Controller.AI.Corpus.Tests.csproj";
    private const string USER_AGENT = "corpus-tests/1.0";
    private const int ATTEMPTS = 3;

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(90);

    #endregion

    #region Fields

    private static readonly ConcurrentDictionary<string, Lazy<(string? Path, string Reason, bool Moved)>> HEADS = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>The path of a crawl head; ignores the calling test when the head cannot be had here (fails it on the build server, and anywhere when the origin's bytes are not the listed ones).</summary>
    /// <param name="name">The head's name in the list.</param>
    /// <returns>The full path of the checked file.</returns>
    public static string Require(string name)
    {
        var (path, reason, moved) = HEADS.GetOrAdd(name, key => new Lazy<(string?, string, bool)>(() => Find(key))).Value;
        if (path != null)
            return path;

        var message = $"the crawl head {name} is not on this machine and was not fetched: {reason}";
        if (moved || string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Fail(message);

        Assert.Ignore(message);
        return "";
    }

    /// <summary>The list as committed: for each head its name, the file's path at the origin, the range and the range's hash.</summary>
    /// <returns>The origin and the entries.</returns>
    public static (string Origin, IReadOnlyList<(string Name, string Path, long Offset, long Length, string Sha256)> Heads) Listing()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(CorpusFixtures.Folder, LISTING)));
        var heads = document.RootElement.GetProperty("heads").EnumerateArray()
            .Select(head => (head.GetProperty("name").GetString()!, head.GetProperty("path").GetString()!, head.GetProperty("offset").GetInt64(), head.GetProperty("length").GetInt64(), head.GetProperty("sha256").GetString()!))
            .ToList();

        return (document.RootElement.GetProperty("origin").GetString()!, heads);
    }

    private static (string? Path, string Reason, bool Moved) Find(string name)
    {
        var (origin, heads) = Listing();
        var head = heads.SingleOrDefault(entry => entry.Name == name);
        if (head.Name == null)
            return (null, $"{LISTING} does not list it.", true);

        var folder = Environment.GetEnvironmentVariable(FOLDER_VARIABLE) is { Length: > 0 } named ? named : DefaultFolder();
        if (folder == null)
            return (null, "the test project's folder was not found above the test output.", false);

        var target = Path.Combine(folder, name);
        if (File.Exists(target) && Hash(File.ReadAllBytes(target)) == head.Sha256)
            return (target, "", false);

        var (bytes, reason) = Fetch(new Uri(origin + head.Path), head.Offset, head.Length);
        if (bytes == null)
            return (null, reason, false);

        var hash = Hash(bytes);
        if (hash != head.Sha256)
            return (null, $"the origin's bytes {head.Offset}-{head.Offset + head.Length - 1} of {head.Path} hash to {hash}, the list says {head.Sha256}.", true);

        // Written beside and moved into place: two test processes may fetch at once.
        Directory.CreateDirectory(folder);
        var partial = target + "." + Guid.NewGuid().ToString("N") + ".part";
        File.WriteAllBytes(partial, bytes);
        File.Move(partial, target, overwrite: true);

        return (target, "", false);
    }

    /// <summary>One range of one file, tried a few times with a growing pause.</summary>
    private static (byte[]? Bytes, string Reason) Fetch(Uri uri, long offset, long length)
    {
        var reason = "";
        using var client = new HttpClient { Timeout = TIMEOUT };

        for (var attempt = 1; attempt <= ATTEMPTS; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
                request.Headers.UserAgent.ParseAdd(USER_AGENT);

                using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    using var body = response.Content.ReadAsStream();
                    using var buffer = new MemoryStream();
                    body.CopyTo(buffer);
                    if (buffer.Length == length)
                        return (buffer.ToArray(), "");

                    reason = $"the origin sent {buffer.Length} bytes of the {length} asked for.";
                }
                else
                {
                    reason = $"the origin answered {(int)response.StatusCode}.";
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
            {
                // The kind of the failure is enough to say the network is away.
                reason = $"no answer from {uri.Host} ({exception.GetType().Name}).";
            }

            if (attempt < ATTEMPTS)
                Thread.Sleep(TimeSpan.FromSeconds(attempt * attempt));
        }

        return (null, reason);
    }

    private static string? DefaultFolder()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, PROJECT_FILE)))
            directory = directory.Parent;

        return directory == null ? null : Path.Combine(directory.FullName, "@Data", "crawl");
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
