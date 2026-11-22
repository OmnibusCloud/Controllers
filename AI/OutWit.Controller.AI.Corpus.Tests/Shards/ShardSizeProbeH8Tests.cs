using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Audit finding H-8 at the size probe: it asked only the sources without a size, and of those the
/// first sixty-four in manifest order, so a manifest's own sizes were never checked and the mean of
/// the first entries stood for all the others. Now the sample is drawn at random by the server,
/// half of it among the sources that do state a size; a size far below the origin's is found, an
/// answered source is held to the origin's word, and a source nobody answered for is counted at
/// the mean and has no size of its own.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed partial class ShardSizeProbeH8Tests
{
    #region Fields

    private CorpusRawOrigin m_origin = null!;
    private ConcurrentBag<int> m_asked = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        // The origin answers a HEAD for /shards/s-<index>-<bytes>.warc.wet.gz with that many bytes.
        m_asked = [];
        m_origin = new CorpusRawOrigin(async (_, head, stream, cancellationToken) =>
        {
            var match = ShardName().Match(head);
            if (!match.Success)
            {
                await stream.WriteAsync(CorpusRawOrigin.Head("404 Not Found", "Content-Length: 0", "Connection: close"), cancellationToken);
                return;
            }

            m_asked.Add(int.Parse(match.Groups[1].Value));
            await stream.WriteAsync(CorpusRawOrigin.Head("200 OK", $"Content-Length: {match.Groups[2].Value}", "Connection: close"), cancellationToken);
        });
    }

    [TearDown]
    public void TearDown()
    {
        m_origin.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task H8ADeclaredSizeFarBelowTheOriginsIsFoundTest()
    {
        // The audit's manifest: "size": 1 for files that are not. Nobody asked the origin.
        var sources = Enumerable.Range(0, 10).Select(index => Source(index, atOrigin: 5_000_000 + index, declared: 1)).ToList();

        var result = await ProbeAsync(sources);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verified, Is.EqualTo(10), "a source that states a size is asked too");
            Assert.That(result.Overdeclared.Select(mismatch => mismatch.Index), Is.EqualTo(Enumerable.Range(0, 10)));
            Assert.That(result.Overdeclared[3], Is.EqualTo(new ShardSizeMismatch(3, 1, 5_000_003)));
            Assert.That(result.Declared, Is.All.EqualTo(1), "the claim stays the manifest's: a node holds the shard to it and refuses it");
            Assert.That(result.Unsized, Is.Zero);
        });
    }

    [Test]
    public async Task H8ADeclarationWithinItsSlackOrAboveTheOriginIsNoLieTest()
    {
        var sources = new List<CorpusSourceData>
        {
            Source(0, atOrigin: 10_000_000, declared: 10_000_000),
            Source(1, atOrigin: 10_000_000, declared: 9_500_000),
            Source(2, atOrigin: 10_000_000, declared: 80_000_000),
            Source(3, atOrigin: 10_000_000, declared: 8_000_000)
        };

        var result = await ProbeAsync(sources);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verified, Is.EqualTo(4));
            Assert.That(result.Overdeclared.Select(mismatch => mismatch.Index), Is.EqualTo(new[] { 3 }), "an eighth above the declaration is allowed; a quarter is not");
            Assert.That(ShardLimits.DeclaredCap(8_000_000), Is.LessThan(10_000_000));
        });
    }

    [Test]
    public async Task H8TheSampleIsNotTheFirstSourcesOfTheManifestTest()
    {
        // 400 sources without a size. The first 64 used to be the ones asked, whatever came after.
        var sources = Enumerable.Range(0, 400).Select(index => Source(index, atOrigin: 1000 + index, declared: 0)).ToList();

        // A draw that takes its candidates three hundred places on: the sample is sources 300 to 363.
        var last = await ProbeAsync(sources, count => Math.Min(count - 1, 300));
        var lastAsked = m_asked.Order().ToList();
        m_asked.Clear();

        // The server's own draw, twice.
        var first = await ProbeAsync(sources);
        var firstAsked = m_asked.Order().ToList();
        m_asked.Clear();
        await ProbeAsync(sources);
        var secondAsked = m_asked.Order().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lastAsked, Has.Count.EqualTo(ShardSizeProbe.MAX_PROBES));
            Assert.That(lastAsked, Is.EqualTo(Enumerable.Range(300, 64)), "the draw decides, not the manifest's order");
            Assert.That(last.Probed, Is.EqualTo(ShardSizeProbe.MAX_PROBES));
            Assert.That(firstAsked, Has.Count.EqualTo(ShardSizeProbe.MAX_PROBES));
            Assert.That(firstAsked, Is.Not.EqualTo(Enumerable.Range(0, 64)), "not the first sixty-four");
            Assert.That(firstAsked, Is.Not.EqualTo(secondAsked), "and not the same sixty-four twice: whoever writes a manifest cannot know which entries are asked");
            Assert.That(first.Unprobed, Is.EqualTo(400 - ShardSizeProbe.MAX_PROBES));
        });
    }

    [TestCase(100, 100, 32, 32)]
    [TestCase(10, 100, 10, 54)]
    [TestCase(100, 5, 59, 5)]
    [TestCase(3, 2, 3, 2)]
    [TestCase(0, 500, 0, 64)]
    [TestCase(500, 0, 64, 0)]
    public async Task H8HalfTheBudgetGoesToEachKindOfSourceTest(int unsized, int sized, int unsizedAsked, int sizedAsked)
    {
        var sources = Enumerable.Range(0, unsized).Select(index => Source(index, atOrigin: 4000, declared: 0))
            .Concat(Enumerable.Range(unsized, sized).Select(index => Source(index, atOrigin: 4000, declared: 4000)))
            .ToList();

        var result = await ProbeAsync(sources);

        Assert.Multiple(() =>
        {
            Assert.That(result.Probed, Is.EqualTo(unsizedAsked), "sources without a size that were asked");
            Assert.That(result.Verified, Is.EqualTo(sizedAsked), "sources with a size that were asked");
            Assert.That(m_asked, Has.Count.EqualTo(unsizedAsked + sizedAsked));
            Assert.That(m_asked.Count, Is.LessThanOrEqualTo(ShardSizeProbe.MAX_PROBES));
        });
    }

    [Test]
    public async Task H8AnAnsweredSourceIsHeldToTheOriginsWordAndTheRestToTheCapTest()
    {
        // 70 sources without a size, each of another size at the origin: 64 are asked.
        var sources = Enumerable.Range(0, 70).Select(index => Source(index, atOrigin: 1_000_000 * (index + 1), declared: 0)).ToList();

        var result = await ProbeAsync(sources);
        var asked = m_asked.ToHashSet();
        var mean = asked.Sum(index => 1_000_000L * (index + 1)) / asked.Count;

        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.Count.EqualTo(64));
            Assert.That(result.MeanBytes, Is.EqualTo(mean));
            Assert.That(result.Unsized, Is.EqualTo(6));
            Assert.That(result.EstimatedBytes, Is.EqualTo(asked.Sum(index => 1_000_000L * (index + 1)) + 6 * mean));

            for (var index = 0; index < sources.Count; index++)
            {
                if (asked.Contains(index))
                {
                    Assert.That(result.Sizes[index], Is.EqualTo(1_000_000L * (index + 1)), $"source {index}: counted at the origin's answer");
                    Assert.That(result.Declared[index], Is.EqualTo(1_000_000L * (index + 1)), $"source {index}: and held to it by the nodes");
                }
                else
                {
                    Assert.That(result.Sizes[index], Is.EqualTo(mean), $"source {index}: counted at the mean of the answers");
                    Assert.That(result.Declared[index], Is.Zero, $"source {index}: no size of its own, held to the cap only");
                }
            }
        });
    }

    [Test]
    public async Task H8ASourceTheOriginDoesNotAnswerForHasNoSizeTest()
    {
        var sources = new List<CorpusSourceData>
        {
            new() { Url = m_origin.Url("missing.warc.wet.gz"), Format = CorpusSourceFormat.Wet },
            Source(1, atOrigin: 9000, declared: 0)
        };

        var result = await ProbeAsync(sources);

        Assert.Multiple(() =>
        {
            Assert.That(result.Probed, Is.EqualTo(1));
            Assert.That(result.Failed, Is.EqualTo(1));
            Assert.That(result.Sizes, Is.EqualTo(new long[] { 9000, 9000 }), "the unanswered one at the mean of the answered");
            Assert.That(result.Declared, Is.EqualTo(new long[] { 0, 9000 }));
            Assert.That(result.Unsized, Is.EqualTo(1));
        });
    }

    [Test]
    public void H8WithoutAProbeTheManifestsOwnSizesAreTheClaimsTest()
    {
        var sources = new List<CorpusSourceData> { Source(0, 0, declared: 500), Source(1, 0, declared: 0), Source(2, 0, declared: -3) };

        var result = ShardSizeProbe.Unasked(sources);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sizes, Is.EqualTo(new long[] { 500, 0, 0 }));
            Assert.That(result.Declared, Is.EqualTo(new long[] { 500, 0, 0 }));
            Assert.That(result.Unsized, Is.EqualTo(2));
            Assert.That(result.Overdeclared, Is.Empty);
        });
    }

    #endregion

    #region Tools

    private async Task<ShardSizeProbeResult> ProbeAsync(IReadOnlyList<CorpusSourceData> sources, Func<int, int>? random = null)
    {
        using var client = ShardHttp.CreateClient(CorpusTestOrigin.Policy);
        return await ShardSizeProbe.ProbeAsync(sources, [m_origin.Host], CorpusTestOrigin.Policy, client, CancellationToken.None, random);
    }

    private CorpusSourceData Source(int index, long atOrigin, long declared)
    {
        return new CorpusSourceData { Url = m_origin.Url($"s-{index}-{atOrigin}.warc.wet.gz"), Format = CorpusSourceFormat.Wet, SizeBytes = declared };
    }

    [GeneratedRegex(@"^HEAD /shards/s-(\d+)-(\d+)\.warc\.wet\.gz ")]
    private static partial Regex ShardName();

    #endregion
}
