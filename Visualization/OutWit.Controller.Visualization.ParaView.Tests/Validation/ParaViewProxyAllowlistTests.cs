using System.Text;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

[TestFixture]
public sealed class ParaViewProxyAllowlistTests
{
    [Test]
    public void EmbeddedAllowlistForThePinnedRuntimeLoadsTest()
    {
        var allowlist = ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES);

        Assert.Multiple(() =>
        {
            Assert.That(allowlist.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_SERIES));
            Assert.That(allowlist.Origin, Is.EqualTo("generated"), "the embedded allowlist must be the artifact generated from the fixture corpus with the pinned runtime");
            Assert.That(allowlist.Proxies, Does.Contain("sources/XMLUnstructuredGridReader"));
            Assert.That(allowlist.Proxies, Does.Contain("sources/PVDReader"));
            Assert.That(allowlist.Proxies, Does.Contain("filters/Contour"));
            Assert.That(allowlist.Proxies, Does.Contain("views/RenderView"));
            Assert.That(allowlist.Proxies, Does.Contain("misc/TimeKeeper"));
            Assert.That(allowlist.Proxies, Does.Contain("sources/LegacyVTKFileReader"), "a .vtk opened beside an OpenFOAM case reads through the legacy reader");
            Assert.That(allowlist.Proxies, Does.Not.Contain("filters/ProgrammableFilter"));
            Assert.That(allowlist.Proxies, Does.Not.Contain("sources/ProgrammableSource"));
        });
    }

    [Test]
    public void EmbeddedAllowlistForThePreviousReleaseLoadsTest()
    {
        var previous = ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.PREVIOUS_SERIES);

        Assert.Multiple(() =>
        {
            Assert.That(previous.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.PREVIOUS_SERIES));
            Assert.That(previous.Origin, Is.EqualTo("generated"));
            Assert.That(previous.Proxies, Does.Contain("views/RenderView"));
        });
    }

    [Test]
    public void ForProducerUnionsThePreviousReleaseAndKeepsTheRuntimeSeriesTest()
    {
        var runtime = new ParaViewProxyAllowlist(ParaViewRuntimeInfo.RUNTIME_SERIES, "test", ["views/RenderView", "cell_locators/StaticCellLocator"],
            new Dictionary<string, IReadOnlyList<string>> { ["Other"] = ["sources/Other"] });
        var previous = ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.PREVIOUS_SERIES);

        var forPrevious = runtime.ForProducer(ParaViewRuntimeInfo.PREVIOUS_MAJOR, ParaViewRuntimeInfo.PREVIOUS_MINOR);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ForProducer(ParaViewRuntimeInfo.RUNTIME_MAJOR, ParaViewRuntimeInfo.RUNTIME_MINOR), Is.SameAs(runtime));
            Assert.That(runtime.ForProducer(5, 13), Is.SameAs(runtime), "an unaccepted series is refused by the compatibility check, not here");
            Assert.That(forPrevious.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_SERIES));
            Assert.That(forPrevious.Proxies, Is.SupersetOf(previous.Proxies));
            Assert.That(forPrevious.Proxies, Does.Contain("cell_locators/StaticCellLocator"));
            Assert.That(forPrevious.PluginProxies[ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME], Does.Contain($"sources/{ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME}"));
            Assert.That(forPrevious.PluginProxies["Other"], Does.Contain("sources/Other"));
        });
    }

    [Test]
    public void PluginProxiesRequireTheirPluginTest()
    {
        var allowlist = new ParaViewProxyAllowlist("6.1", "test", ["views/RenderView"],
            new Dictionary<string, IReadOnlyList<string>> { [ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME] = ["sources/OmnibusCloudFrdReader"] });

        Assert.Multiple(() =>
        {
            Assert.That(allowlist.Allows("sources/OmnibusCloudFrdReader", []), Is.False);
            Assert.That(allowlist.Allows("sources/OmnibusCloudFrdReader", [ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME]), Is.True);
            Assert.That(allowlist.Allows("views/RenderView", []), Is.True);
            Assert.That(allowlist.EffectiveKeys([ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME]), Does.Contain("sources/OmnibusCloudFrdReader"));
            Assert.That(allowlist.EffectiveKeys([]), Does.Not.Contain("sources/OmnibusCloudFrdReader"));
        });
    }

    [Test]
    public void MissingEmbeddedAllowlistIsAnExplicitErrorTest()
    {
        Assert.Throws<InvalidOperationException>(() => ParaViewProxyAllowlist.LoadEmbedded("1.0"));
    }

    [Test]
    public void MalformedDocumentsAreRejectedTest()
    {
        Assert.Throws<InvalidOperationException>(() => ParaViewProxyAllowlist.Load(new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\": 2, \"paraview\": \"6.1\"}"))));
        Assert.Throws<InvalidOperationException>(() => ParaViewProxyAllowlist.Load(new MemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}"))));
    }

    [Test]
    public void BlockedTypesAreNeverAllowlistedTest()
    {
        var allowlist = ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES);
        var leaked = allowlist.Proxies.Where(key => ParaViewProxyPolicy.BLOCKED_PROXY_TYPES.Contains(key.Split('/')[1])).ToList();

        Assert.That(leaked, Is.Empty);
    }
}
