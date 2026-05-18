using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class RenderPreflightVideoDataTests
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
        right.Issues.Add("ffmpeg missing");
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void IsNotEqualDifferentWarningTest()
    {
        var left = CreateDefault();
        var right = CreateDefault();
        right.Warnings.Add("Software encoding may be slow on the current runtime.");
        Assert.That(left, Was.Not.EqualTo(right));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var result = new RenderPreflightVideoData
        {
            CanRender = false,
            RuntimeTarget = "linux-x64",
            Issues = ["VideoOptions.FrameRate must be > 0, got 0."]
        };

        var clone = (RenderPreflightVideoData)result.Clone();

        Assert.That(clone.CanRender, Is.False);
        Assert.That(clone.RuntimeTarget, Is.EqualTo("linux-x64"));
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

    private static RenderPreflightVideoData CreateDefault()
    {
        return new RenderPreflightVideoData
        {
            CanRender = true,
            RuntimeTarget = "windows-x64",
            Issues = [],
            Warnings = []
        };
    }

    #endregion
}
