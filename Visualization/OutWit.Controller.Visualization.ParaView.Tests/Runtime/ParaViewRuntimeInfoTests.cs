using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The pinned runtime registry: derived version strings, the series check, the embedded resources.
/// </summary>
[TestFixture]
public sealed class ParaViewRuntimeInfoTests
{
    #region Tests

    [TestCase("6.1.1", "6.1", true)]
    [TestCase("6.1.1-fake", "6.1", true)]
    [TestCase("paraview version 6.1.0", "6.1", false)]
    [TestCase("6.2.0", "6.1", false)]
    [TestCase("5.13.3", "6.1", false)]
    [TestCase("", "6.1", false)]
    [TestCase(null, "6.1", false)]
    public void RuntimeSeriesCheckTest(string? version, string series, bool expected)
    {
        Assert.That(ParaViewRuntimeInfo.IsSameSeries(version, series), Is.EqualTo(expected));
    }

    [Test]
    public void RuntimeInfoStringsDeriveFromTheNumbersTest()
    {
        Assert.That(ParaViewRuntimeInfo.RUNTIME_SERIES, Is.EqualTo($"{ParaViewRuntimeInfo.RUNTIME_MAJOR}.{ParaViewRuntimeInfo.RUNTIME_MINOR}"));
        Assert.That(ParaViewRuntimeInfo.RUNTIME_VERSION, Is.EqualTo($"{ParaViewRuntimeInfo.RUNTIME_SERIES}.{ParaViewRuntimeInfo.RUNTIME_PATCH}"));
        Assert.That(ParaViewProxyAllowlist.Bundled.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_SERIES));
    }

    [Test]
    public void EmbeddedRunnerAndReaderArePresentTest()
    {
        Assert.That(ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.RUNNER_RESOURCE), Does.Contain("--task-file"));

        var reader = ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.FRD_READER_RESOURCE);
        Assert.That(reader, Is.Not.Null);
        Assert.Multiple(() =>
        {
            // ParaView names a Python plugin after its module-level paraview_plugin_name: the state's
            // plugin requirement, the allowlist key and this constant must all spell the same name.
            Assert.That(reader, Does.Contain($"paraview_plugin_name = \"{ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME}\""));
            Assert.That(reader, Does.Contain($"name=\"{ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME}\""), "the proxy XML name");
            Assert.That(ParaViewRuntimeInfo.BundledReaderVersion(), Is.EqualTo("1.0.1"));
            Assert.That(ParaViewProxyAllowlist.Bundled.PluginProxies, Contains.Key(ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME));
            Assert.That(ParaViewProxyAllowlist.Bundled.PluginProxies[ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME], Is.EqualTo(new[] { $"sources/{ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME}" }));
        });
    }

    #endregion
}
