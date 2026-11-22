using System.Net;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// What a machine fetches from, whatever a task says: the public origins over HTTPS at
/// public addresses, and the hosts its owner listed, taken as written.
/// </summary>
[TestFixture]
public sealed class ShardOriginPolicyTests
{
    #region Tests

    [TestCase("https://data.commoncrawl.org/crawl-data/x.warc.gz")]
    [TestCase("https://DATA.commoncrawl.ORG/x.warc.gz")]
    [TestCase("https://data.commoncrawl.org:443/x.warc.gz")]
    public void PublicOriginPassesOverHttpsTest(string url)
    {
        Assert.That(new ShardOriginPolicy().Check(new Uri(url)), Is.Null);
    }

    [TestCase("http://data.commoncrawl.org/x.warc.gz", "https only")]
    [TestCase("https://data.commoncrawl.org:8443/x.warc.gz", "default port")]
    public void PublicOriginIsRefusedWrittenAnotherWayTest(string url, string reason)
    {
        Assert.That(new ShardOriginPolicy().Check(new Uri(url)), Does.Contain(reason));
    }

    [TestCase("https://evil.example/x.warc.gz")]
    [TestCase("http://127.0.0.1:8080/x.warc.gz")]
    [TestCase("http://localhost/x.warc.gz")]
    [TestCase("http://192.168.1.1/x.warc.gz")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://[::1]/x.warc.gz")]
    [TestCase("https://sub.data.commoncrawl.org/x.warc.gz")]
    [TestCase("https://data.commoncrawl.org.evil.example/x.warc.gz")]
    public void AnyOtherHostIsRefusedUnlessTheOwnerListedItTest(string url)
    {
        var refusal = new ShardOriginPolicy().Check(new Uri(url));

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Does.Contain("not a public origin"));
            Assert.That(refusal, Does.Contain(ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE), "the refusal names the way out");
        });
    }

    [TestCase("http://127.0.0.1:8080/x.warc.gz")]
    [TestCase("http://datalake.corp.example:9000/bucket/x.warc.gz")]
    [TestCase("https://DataLake.corp.example/x.warc.gz")]
    public void OwnersHostsAreTakenAsWrittenTest(string url)
    {
        var policy = new ShardOriginPolicy(["127.0.0.1", "datalake.corp.example"]);

        Assert.That(policy.Check(new Uri(url)), Is.Null);
    }

    [TestCase(null, 0)]
    [TestCase("", 0)]
    [TestCase("   ", 0)]
    [TestCase("127.0.0.1", 1)]
    [TestCase("a.example;b.example", 2)]
    [TestCase("a.example, b.example  c.example", 3)]
    [TestCase("https://a.example/path;a.example:8080;A.EXAMPLE", 1)]
    public void VariableListsHostsTest(string? variable, int hosts)
    {
        Assert.That(ShardOriginPolicy.Parse(variable).ExtraOrigins, Has.Count.EqualTo(hosts));
    }

    [Test]
    public void OwnersListingDoesNotLoosenThePublicOriginsTest()
    {
        var policy = new ShardOriginPolicy(["datalake.corp.example"]);

        Assert.That(policy.Check(new Uri("http://data.commoncrawl.org/x.warc.gz")), Does.Contain("https only"));
    }

    [Test]
    public void PublicOriginResolvingIntoTheMachinesNetworkIsRefusedTest()
    {
        var policy = new ShardOriginPolicy(null, (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.5"), IPAddress.Loopback }));

        var exception = Assert.ThrowsAsync<ShardOriginRefusedException>(() => policy.ResolveAsync("data.commoncrawl.org", CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("no public address"));
    }

    [Test]
    public async Task OnlyThePublicAddressesOfAPublicOriginAreUsedTest()
    {
        var open = IPAddress.Parse("13.32.10.20");
        var policy = new ShardOriginPolicy(null, (_, _) => Task.FromResult(new[] { IPAddress.Parse("192.168.0.9"), open, IPAddress.IPv6Loopback }));

        var addresses = await policy.ResolveAsync("data.commoncrawl.org", CancellationToken.None);

        Assert.That(addresses, Is.EqualTo(new[] { open }));
    }

    [Test]
    public async Task OwnersHostKeepsItsPrivateAddressTest()
    {
        var inside = IPAddress.Parse("10.20.30.40");
        var policy = new ShardOriginPolicy(["datalake.corp.example"], (_, _) => Task.FromResult(new[] { inside }));

        var addresses = await policy.ResolveAsync("datalake.corp.example", CancellationToken.None);

        Assert.That(addresses, Is.EqualTo(new[] { inside }));
    }

    [Test]
    public void AddressLiteralIsJudgedWithoutALookupTest()
    {
        var policy = new ShardOriginPolicy(null, (_, _) => throw new InvalidOperationException("a literal needs no lookup"));

        Assert.ThrowsAsync<ShardOriginRefusedException>(() => policy.ResolveAsync("169.254.169.254", CancellationToken.None));
    }

    #endregion
}
