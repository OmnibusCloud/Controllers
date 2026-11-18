using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Audit finding M-4: the fetch gave up for good after three attempts in about nine seconds, did
/// not read <c>Retry-After</c>, took a body that ended early without a length for a complete
/// shard, and had no deadline for a body that trickles. Every origin here is a loopback one.
/// </summary>
[TestFixture]
public sealed class ShardFetcherM4Tests
{
    #region Constants

    private const string WET_FIXTURE = CorpusFixtures.WET;

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_m4_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task M4AnOriginThatAsksToSlowDownIsWaitedForTest()
    {
        // Five answers of 503, then the shard: three attempts gave up at the third.
        using var origin = new CorpusTestOrigin(Fixtures());
        var fetcher = new ShardFetcher(new ShardFetchPolicy(3, TimeSpan.Zero, TimeSpan.FromSeconds(60), ThrottleAttempts: 6, ThrottleDelay: TimeSpan.FromMilliseconds(5), MaxDelay: TimeSpan.FromSeconds(1)), CorpusTestOrigin.Policy);
        var destination = Path.Combine(m_directory, "patient.gz");
        var before = origin.Requests;

        var fetched = await fetcher.FetchAsync(new Uri(origin.FlakyUrl(WET_FIXTURE, 5)), [origin.Host], destination, 0, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.EqualTo(new FileInfo(Path.Combine(Fixtures(), WET_FIXTURE)).Length));
            Assert.That(origin.Requests - before, Is.EqualTo(6));
            Assert.That(ShardFetchPolicy.Default.ThrottleAttempts, Is.EqualTo(6));
            Assert.That(ShardFetchPolicy.Default.ThrottleDelay, Is.EqualTo(TimeSpan.FromSeconds(5)), "5, 10, 20, 40, 80 s between the six: over two and a half minutes of patience");
            Assert.That(ShardFetchPolicy.Default.MaxDelay, Is.EqualTo(TimeSpan.FromMinutes(2)));
        });
    }

    [Test]
    public void M4AnOriginThatNeverStopsAskingIsAVerdictWithItsStatusTest()
    {
        using var origin = new CorpusTestOrigin(Fixtures());
        var fetcher = new ShardFetcher(new ShardFetchPolicy(3, TimeSpan.Zero, TimeSpan.FromSeconds(60), ThrottleAttempts: 4), CorpusTestOrigin.Policy);
        var before = origin.Requests;

        var exception = Assert.ThrowsAsync<ShardFetchException>(() => fetcher.FetchAsync(new Uri(origin.FlakyUrl(WET_FIXTURE, 100)), [origin.Host], Path.Combine(m_directory, "never.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That((int?)exception!.Status, Is.EqualTo(503));
            Assert.That(origin.Requests - before, Is.EqualTo(4), "the policy's patience, no more");
        });
    }

    [Test]
    public async Task M4RetryAfterIsHonouredWithinTheCeilingTest()
    {
        var body = await File.ReadAllBytesAsync(Path.Combine(Fixtures(), WET_FIXTURE));
        using var origin = new CorpusRawOrigin(async (request, _, stream, cancellationToken) =>
        {
            if (request == 1)
                await stream.WriteAsync(CorpusRawOrigin.Head("503 Slow Down", "Retry-After: 1", "Content-Length: 0", "Connection: close"), cancellationToken);
            else if (request == 2)
                await stream.WriteAsync(CorpusRawOrigin.Head("429 Too Many Requests", "Retry-After: 3600", "Content-Length: 0", "Connection: close"), cancellationToken);
            else
            {
                await stream.WriteAsync(CorpusRawOrigin.Head("200 OK", $"Content-Length: {body.Length}", "Connection: close"), cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
            }
        });
        var fetcher = new ShardFetcher(new ShardFetchPolicy(3, TimeSpan.Zero, TimeSpan.FromSeconds(60), ThrottleAttempts: 6, ThrottleDelay: TimeSpan.FromMilliseconds(1), MaxDelay: TimeSpan.FromMilliseconds(1500)), CorpusTestOrigin.Policy);

        var clock = Stopwatch.StartNew();
        var fetched = await fetcher.FetchAsync(new Uri(origin.Url("x.gz")), [origin.Host], Path.Combine(m_directory, "asked.gz"), 0, CancellationToken.None);
        var elapsed = clock.Elapsed;

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.EqualTo(body.Length));
            Assert.That(origin.Requests, Is.EqualTo(3));
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(2400)), "one second as the origin asked, then the ceiling in place of its hour");
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(20)), "an hour is not waited for");
        });
    }

    [Test]
    public void M4ABodyThatTricklesIsAbandonedTest()
    {
        // A byte every 50 ms never stalls; at 100 kB/s with 200 ms of slack, 10,000 bytes have 300 ms.
        using var origin = new CorpusRawOrigin(async (_, _, stream, cancellationToken) =>
        {
            await stream.WriteAsync(CorpusRawOrigin.Head("200 OK", "Content-Length: 10000", "Connection: close"), cancellationToken);
            for (var sent = 0; sent < 10000; sent++)
            {
                await stream.WriteAsync(new byte[] { 0x1F }, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
        });
        var fetcher = new ShardFetcher(new ShardFetchPolicy(2, TimeSpan.Zero, TimeSpan.FromSeconds(60), MinBytesPerSecond: 100_000, Slack: TimeSpan.FromMilliseconds(200)), CorpusTestOrigin.Policy);

        var clock = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<ShardFetchException>(() => fetcher.FetchAsync(new Uri(origin.Url("slow.gz")), [origin.Host], Path.Combine(m_directory, "slow.gz"), 0, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Status, Is.Null, "nobody refused: the body did not come");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)), "two attempts of about 300 ms, not 500 s of a byte at a time");
            Assert.That(ShardFetchPolicy.Default.MinBytesPerSecond, Is.EqualTo(32 * 1024));
        });
    }

    [Test]
    public async Task M4ABodyThatEndsEarlyWithoutALengthIsNotACompletedShardTest()
    {
        // A close-delimited body cut inside a record: the reader's "honest reading of a partial
        // download" made a shorter product with verdict Completed.
        var whole = WarcSynth.Shard(Enumerable.Range(0, 40).Select(index => WarcSynth.ConversionRecord($"https://a.example/{index}", WarcSynth.Paragraphs(new Random(index), 300))));
        var cut = whole[..(whole.Length - whole.Length / 3)];

        var truncated = await RunAsync(cut, withLength: false);
        var complete = await RunAsync(whole, withLength: false);

        Assert.Multiple(() =>
        {
            Assert.That(truncated.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(truncated.Error, Is.EqualTo(CorpusResultCodes.BODY_TRUNCATED));
            Assert.That(truncated.Stats, Is.Null, "no product of a part of a shard");
            Assert.That(complete.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), "a body without a length that is whole is the shard");
            Assert.That(complete.Stats!.RecordsIn, Is.EqualTo(40));
        });
    }

    [Test]
    public async Task M4AFileThatIsTruncatedAtItsOriginIsCompletedWithANoteTest()
    {
        // The origin states the length it has and gives all of it: the shard itself ends inside a
        // record, the same for every node, and the records before the cut are its product.
        var whole = WarcSynth.Shard(Enumerable.Range(0, 40).Select(index => WarcSynth.ConversionRecord($"https://a.example/{index}", WarcSynth.Paragraphs(new Random(index), 300))));
        var cut = whole[..(whole.Length - whole.Length / 3)];

        var result = await RunAsync(cut, withLength: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.NOTE_TRUNCATED), "a code, not a sentence");
            Assert.That(result.Stats!.RecordsIn, Is.InRange(1, 39));
        });
    }

    [Test]
    public async Task M4AFileCutInsideARecordsHeadersIsTruncatedAndNotMalformedTest()
    {
        // The cut is in the headers of the 28th record, after the name of a header and before
        // its colon. The reader took the half line for a malformed header: a partial download
        // was reported as a file that is no WARC, and a file cut there at its origin made no
        // product of its 27 whole records.
        var records = Enumerable.Range(0, 40).Select(index => WarcSynth.ConversionRecord($"https://a.example/{index}", WarcSynth.Paragraphs(new Random(index), 300))).ToList();
        byte[] cut = [.. records.Take(27).SelectMany(record => record), .. "WARC/1.0\r\nWARC-Type: conversion\r\nWARC-Target"u8];

        var stated = await RunAsync(cut, withLength: true);
        var unstated = await RunAsync(cut, withLength: false);

        Assert.Multiple(() =>
        {
            Assert.That(stated.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), "the origin's file ends there: the same for every node");
            Assert.That(stated.Error, Is.EqualTo(CorpusResultCodes.NOTE_TRUNCATED));
            Assert.That(stated.Stats!.RecordsIn, Is.EqualTo(27));
            Assert.That(unstated.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed), "a body without a length that ends in a half line did not arrive");
            Assert.That(unstated.Error, Is.EqualTo(CorpusResultCodes.BODY_TRUNCATED));
            Assert.That(unstated.Stats, Is.Null);
        });
    }

    #endregion

    #region Tools

    private async Task<CorpusResultData> RunAsync(byte[] body, bool withLength)
    {
        using var origin = new CorpusRawOrigin(async (_, _, stream, cancellationToken) =>
        {
            var head = withLength
                ? CorpusRawOrigin.Head("200 OK", $"Content-Length: {body.Length}", "Connection: close")
                : CorpusRawOrigin.Head("200 OK", "Connection: close");
            await stream.WriteAsync(head, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        });

        var task = new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = origin.Url("s.warc.wet.gz"), Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "m4", IdentifyLanguage = false, FilterQuality = false },
            AllowedOrigins = [origin.Host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New()
        };

        var root = Path.Combine(m_directory, Guid.NewGuid().ToString("N"));
        var blobs = new CorpusTestBlobService(Path.Combine(root, "blobs"));
        return await ShardTaskRunner.RunAsync(task, new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy), blobs, Path.Combine(root, "work"), CancellationToken.None);
    }

    private static string Fixtures()
    {
        return CorpusFixtures.Folder;
    }

    #endregion
}
