using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The size probe runs on the machine that plans a job — the server — so it obeys that
/// machine's policy: a manifest cannot make the server send a request to a host its owner
/// did not agree to, and a redirect cannot lead it there either.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardSizeProbeTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";

    #endregion

    #region Fields

    private CorpusTestOrigin m_origin = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_origin = new CorpusTestOrigin(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_origin.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task SourceOfAListedHostIsAskedItsSizeTest()
    {
        using var client = ShardHttp.CreateClient(CorpusTestOrigin.Policy);
        var expected = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE)).Length;

        var result = await ShardSizeProbe.ProbeAsync([Source(m_origin.ShardUrl(WET_FIXTURE))], [m_origin.Host], CorpusTestOrigin.Policy, client);

        Assert.Multiple(() =>
        {
            Assert.That(result.Probed, Is.EqualTo(1));
            Assert.That(result.EstimatedBytes, Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task SourceOutsideThePlanningMachinesPolicyIsNotAskedTest()
    {
        // The manifest names the loopback origin; the planning machine's owner did not.
        var strict = new ShardOriginPolicy();
        using var client = ShardHttp.CreateClient(strict);
        var before = m_origin.Requests;

        var result = await ShardSizeProbe.ProbeAsync([Source(m_origin.ShardUrl(WET_FIXTURE)), Source($"{m_origin.BaseUrl}forbidden")], [m_origin.Host], strict, client);

        Assert.Multiple(() =>
        {
            Assert.That(m_origin.Requests, Is.EqualTo(before), "no request leaves the machine");
            Assert.That(result.Probed, Is.Zero);
            Assert.That(result.Failed, Is.Zero, "a source that was not asked did not fail to answer");
            Assert.That(result.Unprobed, Is.EqualTo(2));
            Assert.That(result.EstimatedBytes, Is.Zero);
        });
    }

    [Test]
    public async Task RedirectOutOfTheAllowlistGivesNoSizeTest()
    {
        using var client = ShardHttp.CreateClient(CorpusTestOrigin.Policy);
        var before = m_origin.Requests;

        var sizes = await ShardSizeProbe.ProbeSizesAsync([Source(m_origin.HopUrl("http://169.254.169.254/latest/meta-data/"))], [m_origin.Host], CorpusTestOrigin.Policy, client);

        Assert.Multiple(() =>
        {
            Assert.That(sizes, Is.EqualTo(new long?[] { null }));
            Assert.That(m_origin.Requests - before, Is.EqualTo(1), "the first hop only");
        });
    }

    #endregion

    #region Tools

    private static CorpusSourceData Source(string url)
    {
        return new CorpusSourceData { Url = url, Format = CorpusSourceFormat.Wet };
    }

    #endregion
}
