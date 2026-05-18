using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderOptionsDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualTest()
    {
        var opts = CreateDefault();
        Assert.That(opts, Was.EqualTo(opts.Clone()));
    }

    [Test]
    public void IsNotEqualDifferentFormatTest()
    {
        var opts1 = CreateDefault();
        var opts2 = new RenderOptionsData
        {
            Format = RenderFormat.EXR,
            Engine = RenderEngine.Cycles
        };
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    [Test]
    public void IsNotEqualDifferentEngineTest()
    {
        var opts1 = CreateDefault();
        var opts2 = CreateDefault();
        opts2.Engine = RenderEngine.Eevee;
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    [Test]
    public void IsNotEqualDifferentDenoiseTest()
    {
        var opts1 = CreateDefault();
        var opts2 = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Denoise = true
        };
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    [Test]
    public void IsNotEqualDifferentSamplesTest()
    {
        var opts1 = CreateDefault();
        var opts2 = CreateDefault();
        opts2.Samples = 256;
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var opts = new RenderOptionsData
        {
            Format = RenderFormat.EXR,
            Engine = RenderEngine.GreasePencil,
            Samples = 512,
            ResolutionX = 3840,
            ResolutionY = 2160,
            Denoise = true
        };

        var clone = (RenderOptionsData)opts.Clone();

        Assert.That(clone.Format, Is.EqualTo(RenderFormat.EXR));
        Assert.That(clone.Engine, Is.EqualTo(RenderEngine.GreasePencil));
        Assert.That(clone.Samples, Is.EqualTo(512));
        Assert.That(clone.ResolutionX, Is.EqualTo(3840));
        Assert.That(clone.ResolutionY, Is.EqualTo(2160));
        Assert.That(clone.Denoise, Is.True);
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var opts = new RenderOptionsData
        {
            Format = RenderFormat.JPEG,
            Engine = RenderEngine.Eevee,
            Samples = 128,
            ResolutionX = 1920,
            ResolutionY = 1080,
            Denoise = true
        };

        var clone = opts.MemoryPackClone();

        Assert.That(clone, Was.EqualTo(opts));
    }

    #endregion

    #region Tools

    private static RenderOptionsData CreateDefault()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles
        };
    }

    #endregion
}
