using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class RenderRuntimeDiagnosticsDataTests
{
    #region Tests

    [Test]
    public void IsEqualTest()
    {
        var diagnostics = CreateDefault();
        Assert.That(diagnostics, Was.EqualTo(diagnostics.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentBlendSupportTest()
    {
        var left = CreateDefault();
        var right = CreateDefault();
        right.SupportsAlphaBlend = false;
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var diagnostics = new RenderRuntimeDiagnosticsData
        {
            RuntimeTarget = "windows-x64",
            BlenderAvailable = true,
            BlenderVersion = "Blender 4.2.0",
            AvailableRenderBackends = ["OPTIX", "CUDA"],
            SelectedRenderBackend = "OPTIX",
            UsesGpuForRendering = true,
            RenderBackendSelectionMessage = "Auto-selected OPTIX",
            FfmpegAvailable = true,
            FfmpegVersion = "ffmpeg 7.1",
            FfprobeAvailable = true,
            FfprobeVersion = "ffprobe 7.1",
            SupportsCenterPriorityCrop = true,
            SupportsAlphaBlend = true
        };

        var clone = (RenderRuntimeDiagnosticsData)diagnostics.Clone();

        Assert.That(clone.RuntimeTarget, Is.EqualTo("windows-x64"));
        Assert.That(clone.BlenderAvailable, Is.True);
        Assert.That(clone.AvailableRenderBackends, Is.EqualTo(new[] { "OPTIX", "CUDA" }));
        Assert.That(clone.SelectedRenderBackend, Is.EqualTo("OPTIX"));
        Assert.That(clone.UsesGpuForRendering, Is.True);
        Assert.That(clone.FfprobeVersion, Is.EqualTo("ffprobe 7.1"));
        Assert.That(clone.SupportsAlphaBlend, Is.True);
    }

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var diagnostics = CreateDefault();
        var clone = diagnostics.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(diagnostics));
    }

    #endregion

    #region Tools

    private static RenderRuntimeDiagnosticsData CreateDefault()
    {
        return new RenderRuntimeDiagnosticsData
        {
            RuntimeTarget = "windows-x64",
            BlenderAvailable = true,
            BlenderVersion = "Blender 4.2.0",
            AvailableRenderBackends = ["OPTIX", "CUDA"],
            SelectedRenderBackend = "OPTIX",
            UsesGpuForRendering = true,
            RenderBackendSelectionMessage = "Auto-selected OPTIX",
            FfmpegAvailable = true,
            FfmpegVersion = "ffmpeg 7.1",
            FfprobeAvailable = true,
            FfprobeVersion = "ffprobe 7.1",
            SupportsCenterPriorityCrop = true,
            SupportsAlphaBlend = true
        };
    }

    #endregion
}
