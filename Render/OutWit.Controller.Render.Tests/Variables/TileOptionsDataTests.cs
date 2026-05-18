using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public sealed class TileOptionsDataTests
{
    #region Tests

    [Test]
    public void IsEqualTest()
    {
        var options = CreateDefault();
        Assert.That(options, Was.EqualTo(options.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentOverlapTest()
    {
        var options1 = CreateDefault();
        var options2 = CreateDefault();
        options2.OverlapPx = 8;
        Assert.That(options1, Was.Not.EqualTo(options2));
    }

    [Test]
    public void IsNotEqualDifferentBlendModeTest()
    {
        var options1 = CreateDefault();
        var options2 = CreateDefault();
        options2.BlendMode = TileBlendMode.AlphaBlend;
        Assert.That(options1, Was.Not.EqualTo(options2));
    }

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var options = new TileOptionsData
        {
            OverlapPx = 12,
            BlendMode = TileBlendMode.AlphaBlend
        };

        var clone = (TileOptionsData)options.Clone();

        Assert.That(clone.OverlapPx, Is.EqualTo(12));
        Assert.That(clone.BlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
    }

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var options = new TileOptionsData
        {
            OverlapPx = 6,
            BlendMode = TileBlendMode.AlphaBlend
        };

        var clone = options.MemoryPackClone();

        Assert.That(clone, Was.EqualTo(options));
    }

    #endregion

    #region Tools

    private static TileOptionsData CreateDefault()
    {
        return new TileOptionsData
        {
            OverlapPx = 0,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    #endregion
}
