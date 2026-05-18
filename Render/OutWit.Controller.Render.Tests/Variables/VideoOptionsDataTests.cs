using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class VideoOptionsDataTests
{
    #region Tests

    [Test]
    public void IsEqualTest()
    {
        var options = CreateDefault();
        Assert.That(options, Was.EqualTo(options.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentFrameRateTest()
    {
        var options1 = CreateDefault();
        var options2 = CreateDefault();
        options2.FrameRate = 60;
        Assert.That(options1, Was.Not.EqualTo(options2));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var options = new VideoOptionsData
        {
            FrameRate = 30,
            ConstantRateFactor = 18
        };

        var clone = (VideoOptionsData)options.Clone();

        Assert.That(clone.FrameRate, Is.EqualTo(30));
        Assert.That(clone.ConstantRateFactor, Is.EqualTo(18));
    }

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var options = new VideoOptionsData
        {
            FrameRate = 25,
            ConstantRateFactor = 20
        };

        var clone = options.MemoryPackClone();

        Assert.That(clone, Was.EqualTo(options));
    }

    #endregion

    #region Tools

    private static VideoOptionsData CreateDefault()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }

    #endregion
}
