using System.Diagnostics;
using System.Net;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// A node's own network is no statement about a shard. Before the answer's head there was no
/// limit at all: an origin that took the request and never answered held a download slot until
/// the job was cancelled, on every hop of a redirect. And three things a node meets on its own
/// link were final verdicts on the shard, compared across copies and never run elsewhere: a
/// server error or a request to slow down that outlasted the retries (a gateway or a proxy in
/// front of the node says them as often as the origin), and a public origin's name that the
/// node's resolver turned into a private address. Every origin here is a loopback one; the
/// resolver is the test's own.
/// </summary>
[TestFixture]
public sealed class ShardFetcherNodeNetworkTests
{
    #region Constants

    /// <summary>The stall limit of the tests' policy: the head has as long.</summary>
    private static readonly TimeSpan STALL = TimeSpan.FromMilliseconds(500);

    #endregion

    #region Fields

    private string m_directory = null!;
    private CorpusTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_network_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_blobs = new CorpusTestBlobService(Path.Combine(m_directory, "blobs"));
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Answer Head Tests

    [Test]
    public void AnOriginThatNeverSendsItsAnswerHeadGivesUpAtTheStallLimitTest()
    {
        using var origin = new CorpusRawOrigin(async (_, _, _, cancellationToken) => await Task.Delay(Timeout.Infinite, cancellationToken));
        using var job = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clock = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<ShardFetchException>(() => Fetcher().FetchAsync(new Uri(origin.Url("silent.gz")), [origin.Host], Path.Combine(m_directory, "silent.gz"), 0, job.Token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Status, Is.Null, "nobody answered");
            Assert.That(exception.MayBeTheNodes, Is.True);
            Assert.That(origin.Requests, Is.EqualTo(2), "each attempt asked once and was given up at the stall limit");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)), "the job's own deadline was not what ended it");
        });
    }

    [Test]
    public void ARedirectToAHopThatNeverAnswersGivesUpAtTheStallLimitTest()
    {
        using var origin = new CorpusRawOrigin(async (request, _, stream, cancellationToken) =>
        {
            if (request % 2 == 1)
                await stream.WriteAsync(CorpusRawOrigin.Head("302 Found", "Location: /dark.gz", "Content-Length: 0", "Connection: close"), cancellationToken);
            else
                await Task.Delay(Timeout.Infinite, cancellationToken);
        });
        using var job = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clock = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<ShardFetchException>(() => Fetcher().FetchAsync(new Uri(origin.Url("moved.gz")), [origin.Host], Path.Combine(m_directory, "moved.gz"), 0, job.Token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Status, Is.Null);
            Assert.That(origin.Requests, Is.EqualTo(4), "two attempts, each a redirect and a hop that went dark");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
        });
    }

    [Test]
    public async Task AShardWhoseOriginNeverAnswersIsAFaultOfTheNodeTest()
    {
        using var origin = new CorpusRawOrigin(async (_, _, _, cancellationToken) => await Task.Delay(Timeout.Infinite, cancellationToken));
        using var job = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await ShardTaskRunner.RunAsync(TaskFor(origin.Url("silent.gz"), origin.Host), Fetcher(), m_blobs, Work(), job.Token);

        AssertNetworkFault(result);
    }

    #endregion

    #region Node Network Tests

    [TestCase(500)]
    [TestCase(502)]
    [TestCase(503)]
    [TestCase(504)]
    [TestCase(429)]
    [TestCase(407)]
    public async Task AStatusThatMayBeTheNodesOwnLinkIsAFaultOfTheNodeTest(int status)
    {
        // A gateway in front of the node that answers 502 for as long as the node asks: the
        // shard was FetchFailed/http_502, a verdict no other node was asked to check.
        using var origin = new CorpusRawOrigin(async (_, _, stream, cancellationToken) =>
            await stream.WriteAsync(CorpusRawOrigin.Head($"{status} Not Now", "Content-Length: 0", "Connection: close"), cancellationToken));

        var result = await ShardTaskRunner.RunAsync(TaskFor(origin.Url("gated.gz"), origin.Host), Fetcher(), m_blobs, Work(), CancellationToken.None);

        // A request for proxy credentials is not tried again: it will not change by itself.
        if (status == 407)
            Assert.That(origin.Requests, Is.EqualTo(1));
        else
            Assert.That(origin.Requests, Is.GreaterThanOrEqualTo(2), "tried again before giving up");
        AssertNetworkFault(result);
    }

    [TestCase(403)]
    [TestCase(404)]
    [TestCase(410)]
    public async Task AnOriginThatRefusesTheShardIsStillAVerdictTest(int status)
    {
        using var origin = new CorpusRawOrigin(async (_, _, stream, cancellationToken) =>
            await stream.WriteAsync(CorpusRawOrigin.Head($"{status} No", "Content-Length: 0", "Connection: close"), cancellationToken));

        var result = await ShardTaskRunner.RunAsync(TaskFor(origin.Url("refused.gz"), origin.Host), Fetcher(), m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.Http(status)));
            Assert.That(origin.Requests, Is.EqualTo(1), "the origin's last word");
        });
    }

    [Test]
    public async Task APublicOriginsNameThatResolvesHereToNoPublicAddressIsAFaultOfTheNodeTest()
    {
        // A filtering resolver or a captive network answers a private address for Common Crawl's
        // host. The shard was OriginRefused/origin_address, a verdict. The resolver is the test's;
        // nothing is connected to.
        var asked = 0;
        var policy = new ShardOriginPolicy(null, (_, _) =>
        {
            Interlocked.Increment(ref asked);
            return System.Threading.Tasks.Task.FromResult(new[] { IPAddress.Parse("10.0.0.1") });
        });
        var fetcher = new ShardFetcher(new ShardFetchPolicy(2, TimeSpan.Zero, STALL), policy);
        var host = ShardOriginPolicy.PUBLIC_ORIGINS[0];

        var result = await ShardTaskRunner.RunAsync(TaskFor($"https://{host}/crawl-data/x.warc.wet.gz", host), fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.That(asked, Is.GreaterThanOrEqualTo(1), "the node's resolver gave the answer");
        AssertNetworkFault(result);
        Assert.That(CorpusResultCodes.IsKnown(CorpusShardVerdict.OriginRefused, CorpusResultCodes.ORIGIN_ADDRESS), Is.False, "no node says it as a verdict");
    }

    [Test]
    public void TheStatusesANodeMayMeetOnItsOwnLinkAreNamedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ShardFetchException(HttpStatusCode.BadGateway).MayBeTheNodes, Is.True);
            Assert.That(new ShardFetchException(HttpStatusCode.TooManyRequests).MayBeTheNodes, Is.True);
            Assert.That(new ShardFetchException(HttpStatusCode.ProxyAuthenticationRequired).MayBeTheNodes, Is.True);
            Assert.That(new ShardFetchException(1, null).MayBeTheNodes, Is.True);
            Assert.That(new ShardFetchException(HttpStatusCode.NotFound).MayBeTheNodes, Is.False);
            Assert.That(new ShardFetchException(HttpStatusCode.Forbidden).MayBeTheNodes, Is.False);
            Assert.That(new ShardFetchException(HttpStatusCode.MultipleChoices).MayBeTheNodes, Is.False);
        });
    }

    #endregion

    #region Tools

    /// <summary>Two attempts, no waits, the tests' stall limit; throttled answers twice.</summary>
    private static ShardFetcher Fetcher()
    {
        return new ShardFetcher(new ShardFetchPolicy(2, TimeSpan.Zero, STALL, ThrottleAttempts: 2), CorpusTestOrigin.Policy);
    }

    private static CorpusTaskData TaskFor(string url, string host)
    {
        return new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = url, Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "network", IdentifyLanguage = false },
            AllowedOrigins = [host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
    }

    private string Work()
    {
        return Path.Combine(m_directory, "work", Guid.NewGuid().ToString("N"));
    }

    private static void AssertNetworkFault(CorpusResultData result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault), result.Error);
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.FAULT_NETWORK));
            Assert.That(result.Stats, Is.Null);
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
        });
    }

    #endregion
}
