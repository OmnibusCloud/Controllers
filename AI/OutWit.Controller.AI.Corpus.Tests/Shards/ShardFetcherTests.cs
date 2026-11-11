using System.Net;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The fetcher against a loopback origin: a shard arrives whole, 503s are retried and a 403 is
/// not, the byte cap stops a body with and without a Content-Length, and a dead port is a
/// fetch failure after the policy's attempts.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardFetcherTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";

    #endregion

    #region Fields

    private CorpusTestOrigin m_origin = null!;
    private ShardFetcher m_fetcher = null!;
    private string m_directory = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_origin = new CorpusTestOrigin(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures"));
        m_fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_fetch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_origin.Dispose();
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Redirect Tests

    [Test]
    public async Task RedirectInsideTheAllowlistIsFollowedTest()
    {
        var destination = Path.Combine(m_directory, "hop.gz");
        var expected = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE)).Length;

        // A relative and an absolute redirect, one after the other.
        var url = m_origin.HopUrl(m_origin.HopUrl($"/shards/{WET_FIXTURE}"));
        var fetched = await m_fetcher.FetchAsync(new Uri(url), destination, 0, CancellationToken.None);

        Assert.That(fetched, Is.EqualTo(expected));
    }

    [TestCase("http://localhost:{0}/shards/x.gz", "not an allowed origin", Description = "a host the task does not name")]
    [TestCase("ftp://127.0.0.1/shards/x.gz", "only http and https", Description = "another scheme")]
    [TestCase("http://user:secret@127.0.0.1:{0}/shards/x.gz", "user name or password", Description = "credentials")]
    public void RedirectOutOfTheAllowlistIsRefusedTest(string target, string reason)
    {
        var before = m_origin.Requests;
        var url = m_origin.HopUrl(string.Format(target, m_origin.Port));

        var exception = Assert.ThrowsAsync<ShardOriginRefusedException>(() => m_fetcher.FetchAsync(new Uri(url), Path.Combine(m_directory, "out.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(reason));
            Assert.That(m_origin.Requests - before, Is.EqualTo(1), "the redirect is refused once, not retried");
        });
    }

    [Test]
    public void RedirectToAHostTheMachineDoesNotFetchFromIsRefusedTest()
    {
        // The task names both hosts; the machine's owner listed only the loopback one.
        var url = m_origin.HopUrl("http://192.168.1.1/router/status");

        var exception = Assert.ThrowsAsync<ShardOriginRefusedException>(() => m_fetcher.FetchAsync(new Uri(url), ["127.0.0.1", "192.168.1.1"], Path.Combine(m_directory, "lan.gz"), 0, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("not a public origin"));
    }

    [Test]
    public void EndlessRedirectsStopTest()
    {
        var before = m_origin.Requests;

        var exception = Assert.ThrowsAsync<ShardOriginRefusedException>(() => m_fetcher.FetchAsync(new Uri(m_origin.LoopUrl()), Path.Combine(m_directory, "loop.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("redirects more than"));
            Assert.That(m_origin.Requests - before, Is.EqualTo(ShardHttp.MAX_REDIRECTS + 1));
        });
    }

    [Test]
    public void PublicOriginThatResolvesIntoTheMachinesNetworkIsNotContactedTest()
    {
        // The name is Common Crawl's, the answer of the resolver is this machine: no connection.
        var before = m_origin.Requests;
        var policy = new ShardOriginPolicy(null, (_, _) => Task.FromResult(new[] { IPAddress.Loopback }));
        var fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, policy);

        var exception = Assert.ThrowsAsync<ShardOriginRefusedException>(() => fetcher.FetchAsync(new Uri("https://data.commoncrawl.org/x.warc.gz"), Path.Combine(m_directory, "rebind.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("no public address"));
            Assert.That(m_origin.Requests, Is.EqualTo(before));
        });
    }

    #endregion

    #region Tests

    [Test]
    public async Task ShardArrivesWholeTest()
    {
        var destination = Path.Combine(m_directory, "whole.gz");
        var expected = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE));

        var fetched = await m_fetcher.FetchAsync(new Uri(m_origin.ShardUrl(WET_FIXTURE)), destination, 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.EqualTo(expected.Length));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task TransientFailuresAreRetriedTest()
    {
        var before = m_origin.Requests;
        var destination = Path.Combine(m_directory, "flaky.gz");

        var fetched = await m_fetcher.FetchAsync(new Uri(m_origin.FlakyUrl(WET_FIXTURE, 2)), destination, 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.GreaterThan(0));
            Assert.That(m_origin.Requests - before, Is.EqualTo(3), "two 503s then the shard");
        });
    }

    [Test]
    public async Task BrokenBodyResumesWithARangeRequestTest()
    {
        var before = m_origin.Requests;
        var ranges = m_origin.RangeRequests;
        var destination = Path.Combine(m_directory, "resumed.gz");
        var expected = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE));

        var fetched = await m_fetcher.FetchAsync(new Uri(m_origin.CutUrl(WET_FIXTURE, 300_000)), destination, 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.EqualTo(expected.Length));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(expected), "the appended tail completes the file byte for byte");
            Assert.That(m_origin.Requests - before, Is.EqualTo(2), "the broken answer and one continuation");
            Assert.That(m_origin.RangeRequests - ranges, Is.EqualTo(1), "the continuation asked for the rest, not the whole");
        });
    }

    [Test]
    public async Task ConcurrencyCeilingHoldsAtTheOriginTest()
    {
        var polite = new ShardFetcher(ShardFetchPolicy.Immediate with { MaxConcurrent = 2 }, CorpusTestOrigin.Policy);
        var before = m_origin.MaxInFlight;

        var fetches = Enumerable.Range(0, 6)
            .Select(i => polite.FetchAsync(new Uri(m_origin.SlowUrl(WET_FIXTURE)), Path.Combine(m_directory, $"polite-{i}.gz"), 0, CancellationToken.None))
            .ToList();
        await Task.WhenAll(fetches);

        Assert.Multiple(() =>
        {
            Assert.That(fetches.Select(f => f.Result), Is.All.GreaterThan(0));
            Assert.That(m_origin.MaxInFlight, Is.LessThanOrEqualTo(Math.Max(before, 2)), "never more than two downloads at once from one fetcher");
        });
    }

    [Test]
    public void TooManyTransientFailuresFailTheFetchTest()
    {
        var before = m_origin.Requests;

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => m_fetcher.FetchAsync(new Uri(m_origin.FlakyUrl(WET_FIXTURE, 10)), Path.Combine(m_directory, "never.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("3 attempt(s)"));
            Assert.That(m_origin.Requests - before, Is.EqualTo(3), "the policy's attempts, no more");
        });
    }

    [Test]
    public void ForbiddenIsFinalTest()
    {
        var before = m_origin.Requests;

        var exception = Assert.ThrowsAsync<HttpRequestException>(() => m_fetcher.FetchAsync(new Uri($"{m_origin.BaseUrl}forbidden"), Path.Combine(m_directory, "forbidden.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(m_origin.Requests - before, Is.EqualTo(1), "a 403 is not retried");
        });
    }

    [Test]
    public void ContentLengthPastTheCapIsRefusedBeforeReadingTest()
    {
        var destination = Path.Combine(m_directory, "capped.gz");

        var exception = Assert.ThrowsAsync<ShardOversizedException>(() => m_fetcher.FetchAsync(new Uri(m_origin.ShardUrl(WET_FIXTURE)), destination, 1000, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Limit, Is.EqualTo(1000));
            Assert.That(File.Exists(destination), Is.False, "nothing is written when the length already exceeds the cap");
        });
    }

    [Test]
    public void StreamingBodyPastTheCapIsCutTest()
    {
        var destination = Path.Combine(m_directory, "unsized.bin");

        Assert.ThrowsAsync<ShardOversizedException>(() => m_fetcher.FetchAsync(new Uri($"{m_origin.BaseUrl}unsized"), destination, 100 * 1024, CancellationToken.None));
        Assert.That(new FileInfo(destination).Length, Is.LessThanOrEqualTo(100 * 1024 + (1 << 20)), "at most one buffer past the cap");
    }

    [Test]
    public void DeadPortIsAFetchFailureTest()
    {
        var port = m_origin.Port == 65535 ? 65534 : m_origin.Port + 1;

        Assert.ThrowsAsync<HttpRequestException>(() => m_fetcher.FetchAsync(new Uri($"http://127.0.0.1:{port}/shards/x.gz"), Path.Combine(m_directory, "dead.gz"), 0, CancellationToken.None));
    }

    [Test]
    public void CancellationWinsOverRetriesTest()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => m_fetcher.FetchAsync(new Uri(m_origin.FlakyUrl(WET_FIXTURE, 10)), Path.Combine(m_directory, "cancelled.gz"), 0, cancelled.Token));
    }

    #endregion
}
