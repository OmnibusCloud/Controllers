using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class RenderPreflightStillTiledDataTests
{
    #region Tests

    [Test]
    public void IsEqualTest()
    {
        var result = CreateDefault();
        Assert.That(result, Was.EqualTo(result.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentIssueTest()
    {
        var left = CreateDefault();
        var right = CreateDefault();
        right.Issues.Add("ffprobe missing");
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void IsNotEqualDifferentWarningTest()
    {
        var left = CreateDefault();
        var right = CreateDefault();
        right.Warnings.Add("Large overlap may increase render cost.");
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var result = new RenderPreflightStillTiledData
        {
            CanRender = false,
            RuntimeTarget = "linux-x64",
            RequestedBlendMode = TileBlendMode.AlphaBlend,
            Issues = ["Packaged ffprobe runtime is required for alpha-blend tiled stitching."]
        };

        var clone = (RenderPreflightStillTiledData)result.Clone();

        Assert.That(clone.CanRender, Is.False);
        Assert.That(clone.RuntimeTarget, Is.EqualTo("linux-x64"));
        Assert.That(clone.RequestedBlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
        Assert.That(clone.Issues, Has.Count.EqualTo(1));
        Assert.That(clone.Warnings, Is.Empty);
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

    private static RenderPreflightStillTiledData CreateDefault()
    {
        return new RenderPreflightStillTiledData
        {
            CanRender = true,
            RuntimeTarget = "windows-x64",
            RequestedBlendMode = TileBlendMode.CenterPriorityCrop,
            Issues = [],
            Warnings = []
        };
    }

    #endregion
}
