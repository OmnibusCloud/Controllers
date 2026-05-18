using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class RenderPreflightDataTests
{
    #region Tests

    [Test]
    public void IsEqualTest()
    {
        var result = CreateDefault();
        Assert.That(result, Was.EqualTo(result.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentAggregateFlagTest()
    {
        var left = CreateDefault();
        var right = CreateDefault();
        right.CanRenderAll = false;
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var result = CreateDefault();
        var clone = (RenderPreflightData)result.Clone();

        Assert.That(clone.RuntimeDiagnostics, Is.Not.Null);
        Assert.That(clone.Still, Is.Not.Null);
        Assert.That(clone.Frames, Is.Not.Null);
        Assert.That(clone.StillTiled, Is.Not.Null);
        Assert.That(clone.Video, Is.Not.Null);
        Assert.That(clone.CanRenderAll, Is.True);
    }

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var result = CreateDefault();
        var clone = result.MemoryPackClone();
        Assert.That(clone, Was.EqualTo(result));
    }

    #endregion

    #region Tools

    private static RenderPreflightData CreateDefault()
    {
        return new RenderPreflightData
        {
            RuntimeDiagnostics = new RenderRuntimeDiagnosticsData
            {
                RuntimeTarget = "windows-x64",
                BlenderAvailable = true,
                BlenderVersion = "Blender 4.2.0",
                FfmpegAvailable = true,
                FfmpegVersion = "ffmpeg 7.1",
                FfprobeAvailable = true,
                FfprobeVersion = "ffprobe 7.1",
                SupportsCenterPriorityCrop = true,
                SupportsAlphaBlend = true
            },
            Still = new RenderPreflightFramesData
            {
                CanRender = true,
                RuntimeTarget = "windows-x64",
                Issues = []
            },
            Frames = new RenderPreflightFramesData
            {
                CanRender = true,
                RuntimeTarget = "windows-x64",
                Issues = []
            },
            StillTiled = new RenderPreflightStillTiledData
            {
                CanRender = true,
                RuntimeTarget = "windows-x64",
                RequestedBlendMode = TileBlendMode.AlphaBlend,
                Issues = []
            },
            Video = new RenderPreflightVideoData
            {
                CanRender = true,
                RuntimeTarget = "windows-x64",
                Issues = []
            },
            CanRenderAll = true
        };
    }

    #endregion
}
