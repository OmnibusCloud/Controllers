using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.State;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

/// <summary>
/// The golden corpus (docs 03, section 8.3): every state the pinned ParaView saved over the fixture
/// data passes validation with the embedded (generated) allowlist, the real XML shape is parsed as the
/// controller expects (root element, views collection, TimeKeeper timeline, files domain, the empty
/// CustomProxyDefinitions element), and the series states split into per-timestep tasks carrying the
/// index, the series anchor and the task's own piece.
/// </summary>
[TestFixture]
public sealed class ParaViewCorpusValidationTests
{
    #region Fields

    private string m_root = null!;

    private ParaViewTestBlobService m_blobs = null!;

    private ParaViewPackageValidator m_validator = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!Directory.Exists(ParaViewCorpus.Root))
            Assert.Ignore("fixture corpus not present");
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_corpus_{Guid.NewGuid():N}");
        m_blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        m_validator = new ParaViewPackageValidator(ParaViewProxyAllowlist.Bundled, ParaViewRuntimeInfo.BundledReaderVersion());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void CorpusWasGeneratedWithThePinnedRuntimeTest()
    {
        Assert.That(ParaViewCorpus.GeneratedWith(), Does.Contain(ParaViewRuntimeInfo.RUNTIME_VERSION));
        Assert.That(ParaViewCorpus.States, Has.Count.GreaterThanOrEqualTo(7));
    }

    [Test]
    public void EveryCorpusStateParsesWithTheRealShapeTest()
    {
        foreach (var stateName in ParaViewCorpus.States)
        {
            var document = ParaViewStateDocument.Parse(ParaViewCorpus.StatePath(stateName));

            Assert.Multiple(() =>
            {
                Assert.That(document.Version, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_VERSION), stateName);
                Assert.That(document.ViewNames, Is.EqualTo(new[] { "RenderView1" }), stateName);
                Assert.That(document.HasCustomProxyDefinitions, Is.False, $"{stateName}: the empty <CustomProxyDefinitions/> every state carries must not count");
                Assert.That(document.Proxies.Select(me => me.Key), Does.Contain("views/RenderView"), stateName);
                Assert.That(document.Proxies.Select(me => me.Key), Does.Contain("misc/TimeKeeper"), stateName);
            });

            // File properties are marked by the files domain, values are logical package paths.
            foreach (var proxy in document.ProxiesInGroup("sources"))
            {
                foreach (var property in proxy.Properties.Where(me => ParaViewProxyPolicy.IsFileProperty(proxy, me)))
                {
                    Assert.That(property.HasFileDomain, Is.True, $"{stateName}: {proxy.Key}.{property.Name} should carry the files domain");
                    foreach (var value in property.Values.Where(me => me.Length > 0))
                        Assert.That(ParaViewLogicalPath.Check(value), Is.Null, $"{stateName}: {proxy.Key}.{property.Name} = {value}");
                }
            }
        }
    }

    [Test]
    public void EveryCorpusStateIsValidWithTheGeneratedAllowlistTest()
    {
        foreach (var stateName in ParaViewCorpus.States)
        {
            var (scene, _) = ParaViewCorpus.BuildScene(stateName, Path.Combine(m_root, Path.GetFileNameWithoutExtension(stateName)), m_blobs);
            var options = new ParaViewOutputOptionsData { Width = 64, Height = 48, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

            var report = m_validator.Validate(scene, options, m_blobs.GetStoredPath(scene.StateBlobId));

            Assert.That(report.IsValid, Is.True, $"{stateName}: {string.Join("; ", report.Errors)}");
            Assert.That(report.ResolvedViewId, Is.EqualTo("RenderView1"), stateName);
            Assert.That(report.ProxyTypes, Is.SubsetOf(ParaViewProxyAllowlist.Bundled.Proxies), stateName);
        }
    }

    [Test]
    public void TimeSeriesStatesResolveTheirTimelineFromTheStateTest()
    {
        var (pvd, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.PVD_SERIES, Path.Combine(m_root, "pvd"), m_blobs);
        var (files, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.FILE_SERIES, Path.Combine(m_root, "files"), m_blobs);
        var all = new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var pvdReport = m_validator.Validate(pvd, all, m_blobs.GetStoredPath(pvd.StateBlobId));
        var filesReport = m_validator.Validate(files, all, m_blobs.GetStoredPath(files.StateBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(pvdReport.IsValid, Is.True, string.Join("; ", pvdReport.Errors));
            Assert.That(pvdReport.TimestepValues, Is.EqualTo(new[] { 0.0, 0.5, 1.0, 1.5, 2.0 }));
            Assert.That(pvdReport.ResolvedTimestepIndices, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(pvdReport.SeriesAnchors, Is.EqualTo(new[] { "data/series/series_000.vti" }));
            Assert.That(filesReport.IsValid, Is.True, string.Join("; ", filesReport.Errors));
            Assert.That(filesReport.TimestepValues, Is.EqualTo(new[] { 0.0, 1.0, 2.0, 3.0, 4.0 }));
            Assert.That(filesReport.SeriesAnchors, Is.EqualTo(new[] { "data/series/series_000.vti" }));
        });
    }

    [Test]
    public void SeriesStatesSplitIntoIndexAnchorAndOwnPieceTest()
    {
        var (pvd, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.PVD_SERIES, Path.Combine(m_root, "pvd"), m_blobs);
        var options = new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };
        var report = m_validator.Validate(pvd, options, m_blobs.GetStoredPath(pvd.StateBlobId));

        var tasks = ParaViewTaskSplitter.Split(pvd, report, options);

        Assert.That(tasks, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(tasks[0].Attachments.Select(me => me.LogicalPath), Is.EquivalentTo(new[] { "data/series/series.pvd", "data/series/series_000.vti" }));
            Assert.That(tasks[3].Attachments.Select(me => me.LogicalPath), Is.EquivalentTo(new[] { "data/series/series.pvd", "data/series/series_000.vti", "data/series/series_003.vti" }));
            Assert.That(tasks[3].TimeValue, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void StaticStatesProduceOneTaskTest()
    {
        var (scene, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.SPHERE_STATIC, Path.Combine(m_root, "sphere"), m_blobs);
        var options = new ParaViewOutputOptionsData { Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };
        var report = m_validator.Validate(scene, options, m_blobs.GetStoredPath(scene.StateBlobId));

        var tasks = ParaViewTaskSplitter.Split(scene, report, options);

        Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        Assert.That(tasks, Has.Count.EqualTo(1));
        Assert.That(tasks[0].Attachments, Is.Empty);
        Assert.That(tasks[0].TimeValue, Is.Null);
    }

    #endregion
}
