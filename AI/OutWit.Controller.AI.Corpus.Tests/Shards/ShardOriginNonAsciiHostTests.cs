using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// A host that is not written in ASCII is refused, by name, at the split and on the node. Left
/// open by batch A (rule R-8, the same result on every node): a host outside ASCII is mapped to
/// its wire form by the machine's own library, and two machines need not map it alike, so one
/// machine could fetch a shard another refuses, or compare a host with the allowlist differently.
/// Written in its ASCII form (xn--...) the same host is a plain string on every machine.
/// </summary>
[TestFixture]
public sealed class ShardOriginNonAsciiHostTests
{
    #region Constants

    private const string UNICODE_HOST = "bücher.example";
    private const string ASCII_HOST = "xn--bcher-kva.example";

    #endregion

    #region Tests

    [TestCase("https://bücher.example/a.warc.wet.gz")]
    [TestCase("https://BÜCHER.example/a.warc.wet.gz")]
    [TestCase("https://data.commoncrawl.org。example/a.warc.wet.gz")]
    [TestCase("https://ⓐ.example/a.warc.wet.gz")]
    [TestCase("https://аpple.example/a.warc.wet.gz")]
    public void NonAsciiHostIsRefusedByNameWhateverTheAllowlistSaysTest(string url)
    {
        // The allowlist names the host in every form a library might map it to.
        string[] allowed = [UNICODE_HOST, ASCII_HOST, "data.commoncrawl.org.example", "a.example", "apple.example", "xn--pple-43d.example"];

        var refusal = ShardOrigin.Check(url, allowed, out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(uri, Is.Null);
            Assert.That(refusal, Does.Contain("a host that is not written in ASCII is not fetched; write it in its ASCII form (xn--...)."));
        });
    }

    [Test]
    public void AHostWrittenWithPercentEscapesIsRefusedTest()
    {
        // The same host hidden in escapes: no library is asked what it might mean.
        var refusal = ShardOrigin.Check("https://b%C3%BCcher.example/a.warc.wet.gz", [UNICODE_HOST, ASCII_HOST, "b%C3%BCcher.example"], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(uri, Is.Null);
            Assert.That(refusal, Is.Not.Null);
        });
    }

    [Test]
    public void TheSameHostInItsAsciiFormIsAPlainHostTest()
    {
        var refusal = ShardOrigin.Check($"https://{ASCII_HOST}/a.warc.wet.gz", [ASCII_HOST], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Null);
            Assert.That(uri!.Host, Is.EqualTo(ASCII_HOST));
            Assert.That(ShardOrigin.IsAllowed(uri, [ASCII_HOST.ToUpperInvariant()]), Is.True, "ASCII names compare without regard to case, on any machine");
        });
    }

    [Test]
    public void NonAsciiOutsideTheHostIsNoRefusalTest()
    {
        // The rule is about the host: a path may hold what it holds.
        var refusal = ShardOrigin.Check("https://data.commoncrawl.org/crawl/bücher/a.warc.wet.gz?q=ü#ü", ["data.commoncrawl.org"], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Null);
            Assert.That(uri, Is.Not.Null);
        });
    }

    [Test]
    public void TheSplitRefusesANonAsciiHostByNameTest()
    {
        var manifest = new CorpusManifest(
            new CorpusPipelineData { Version = "v", IdentifyLanguage = false },
            [UNICODE_HOST, "data.commoncrawl.org"],
            [
                new CorpusSourceData { Url = "https://data.commoncrawl.org/a.warc.wet.gz", Format = CorpusSourceFormat.Wet },
                new CorpusSourceData { Url = $"https://{UNICODE_HOST}/b.warc.wet.gz", Format = CorpusSourceFormat.Wet }
            ],
            []);

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "", new ShardOriginPolicy([UNICODE_HOST, ASCII_HOST]));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty, "no task is made of such a manifest");
            Assert.That(plan.Errors, Has.Some.EqualTo($"allowed_origins: '{UNICODE_HOST}' is not written in ASCII; write the host in its ASCII form (xn--...)."));
            Assert.That(plan.Errors, Has.Some.EqualTo($"sources[1]: '{UNICODE_HOST}': a host that is not written in ASCII is not fetched; write it in its ASCII form (xn--...)."));
            Assert.That(plan.ErrorCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void TheSameManifestInAsciiIsPlannedTest()
    {
        var manifest = new CorpusManifest(
            new CorpusPipelineData { Version = "v", IdentifyLanguage = false },
            [ASCII_HOST],
            [new CorpusSourceData { Url = $"https://{ASCII_HOST}/b.warc.wet.gz", Format = CorpusSourceFormat.Wet }],
            []);

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "", new ShardOriginPolicy([ASCII_HOST]));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks, Has.Count.EqualTo(1));
            Assert.That(plan.Tasks[0].AllowedOrigins, Is.EqualTo(new[] { ASCII_HOST }));
        });
    }

    [Test]
    public async Task ANodeGivenSuchATaskAnswersOriginRefusedWithoutARequestTest()
    {
        // A task is the only authority a node has, and a node does not take its word: a task that
        // was not made by the split (or by a split before this rule) ends the same way on every node.
        var directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_idn_{Guid.NewGuid():N}");
        var fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, new ShardOriginPolicy([UNICODE_HOST, ASCII_HOST]));
        var task = new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = $"https://{UNICODE_HOST}/b.warc.wet.gz", Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "v", IdentifyLanguage = false },
            AllowedOrigins = [UNICODE_HOST, ASCII_HOST],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };

        try
        {
            var result = await ShardTaskRunner.RunAsync(task, fetcher, new CorpusTestBlobService(Path.Combine(directory, "blobs")), Path.Combine(directory, "work"), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.OriginRefused));
                Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.ORIGIN_NOT_ALLOWED), "a fixed code: the host's name stays in the task");
                Assert.That(Directory.Exists(Path.Combine(directory, "work")), Is.False, "nothing was fetched");
            });
        }
        finally
        {
            CorpusTestFolders.Delete(directory);
        }
    }

    #endregion
}
