using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>The allowlist gate: exact hosts, any case, any port or path; nothing but http(s).</summary>
[TestFixture]
public sealed class ShardOriginTests
{
    [TestCase("https://data.commoncrawl.org/crawl-data/x.warc.gz", "data.commoncrawl.org", true)]
    [TestCase("https://DATA.CommonCrawl.org/x.warc.gz", "data.commoncrawl.org", true)]
    [TestCase("http://127.0.0.1:8080/shards/x.gz", "127.0.0.1", true)]
    [TestCase("https://data.commoncrawl.org/x.warc.gz", "https://data.commoncrawl.org/", true)]
    [TestCase("https://data.commoncrawl.org/x.warc.gz", "data.commoncrawl.org:443", true)]
    [TestCase("https://evil.example/x.warc.gz", "data.commoncrawl.org", false)]
    [TestCase("https://data.commoncrawl.org.evil.example/x.warc.gz", "data.commoncrawl.org", false)]
    [TestCase("https://sub.data.commoncrawl.org/x.warc.gz", "data.commoncrawl.org", false)]
    public void HostMatchIsExactTest(string url, string origin, bool allowed)
    {
        var refusal = ShardOrigin.Check(url, [origin], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal == null, Is.EqualTo(allowed), refusal);
            Assert.That(uri != null, Is.EqualTo(allowed));
        });
    }

    [TestCase("file:///C:/Windows/system.ini")]
    [TestCase("ftp://data.commoncrawl.org/x.warc.gz")]
    [TestCase("data.commoncrawl.org/x.warc.gz")]
    [TestCase("")]
    public void OnlyHttpIsFetchedTest(string url)
    {
        var refusal = ShardOrigin.Check(url, ["data.commoncrawl.org", "127.0.0.1"], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Not.Null);
            Assert.That(uri, Is.Null);
        });
    }

    [Test]
    public void EmptyAllowlistRefusesEverythingTest()
    {
        Assert.That(ShardOrigin.Check("https://data.commoncrawl.org/x.warc.gz", [], out _), Is.Not.Null);
    }

    [TestCase("https://user:secret@data.commoncrawl.org/x.warc.gz")]
    [TestCase("https://data.commoncrawl.org@evil.example/x.warc.gz")]
    public void UrlWithCredentialsIsRefusedTest(string url)
    {
        var refusal = ShardOrigin.Check(url, ["data.commoncrawl.org", "evil.example"], out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Does.Contain("user name or password"));
            Assert.That(uri, Is.Null);
        });
    }

    [Test]
    public void TaskCannotWidenWhatTheMachineFetchesFromTest()
    {
        // The task names the host, the machine does not: the task's word is not enough.
        var refusal = ShardOrigin.Check("http://192.168.1.1/admin", ["192.168.1.1"], new ShardOriginPolicy(), out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Does.Contain("not a public origin"));
            Assert.That(uri, Is.Null);
        });
    }

    [Test]
    public void TaskNarrowsWhatTheMachineFetchesFromTest()
    {
        // The machine fetches from Common Crawl, this task does not name it.
        var refusal = ShardOrigin.Check("https://data.commoncrawl.org/x.warc.gz", ["datalake.corp.example"], new ShardOriginPolicy(["datalake.corp.example"]), out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Does.Contain("not an allowed origin"));
            Assert.That(uri, Is.Null);
        });
    }

    [Test]
    public void HostNamedByBothPassesTest()
    {
        var refusal = ShardOrigin.Check("https://data.commoncrawl.org/x.warc.gz", ["data.commoncrawl.org"], new ShardOriginPolicy(), out var uri);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Null);
            Assert.That(uri, Is.Not.Null);
        });
    }

    [TestCase(" Data.CommonCrawl.org ", "data.commoncrawl.org")]
    [TestCase("https://data.commoncrawl.org/", "data.commoncrawl.org")]
    [TestCase("data.commoncrawl.org:443", "data.commoncrawl.org")]
    [TestCase("", "")]
    public void NormalizeKeepsTheHostTest(string origin, string expected)
    {
        Assert.That(ShardOrigin.Normalize(origin), Is.EqualTo(expected));
    }
}
