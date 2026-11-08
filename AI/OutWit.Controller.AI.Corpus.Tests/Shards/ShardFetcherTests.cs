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
        m_fetcher = new ShardFetcher(ShardFetcher.CreateClient(), ShardFetchPolicy.Immediate);
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
        var polite = new ShardFetcher(ShardFetcher.CreateClient(), ShardFetchPolicy.Immediate with { MaxConcurrent = 2 });
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
