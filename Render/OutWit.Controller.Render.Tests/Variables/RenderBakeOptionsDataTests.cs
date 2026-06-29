using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Variables;

[TestFixture]
public class RenderBakeOptionsDataTests
{
    #region Is Tests

    [Test]
    public void IsEqualToCloneTest()
    {
        var opts = CreateDefault();
        Assert.That(opts, Was.EqualTo(opts.Clone()));
    }

    [Test]
    public void DefaultsTest()
    {
        var opts = new RenderBakeOptionsData();
        Assert.That(opts.SimulationKinds, Is.EqualTo("Fluid"));
        Assert.That(opts.CacheFormat, Is.EqualTo("OpenVDB"));
        Assert.That(opts.ResolutionMax, Is.EqualTo(0));
    }

    [Test]
    public void IsNotEqualDifferentKindsTest()
    {
        var opts1 = CreateDefault();
        var opts2 = CreateDefault();
        opts2.SimulationKinds = "Fluid,Cloth";
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    [Test]
    public void IsNotEqualDifferentResolutionTest()
    {
        var opts1 = CreateDefault();
        var opts2 = CreateDefault();
        opts2.ResolutionMax = 128;
        Assert.That(opts1, Was.Not.EqualTo(opts2));
    }

    #endregion

    #region Clone Tests

    [Test]
    public void ClonePreservesAllFieldsTest()
    {
        var opts = new RenderBakeOptionsData
        {
            SimulationKinds = "Fluid,Cloth",
            CacheFormat = "Alembic",
            ResolutionMax = 256
        };

        var clone = (RenderBakeOptionsData)opts.Clone();

        Assert.That(clone.SimulationKinds, Is.EqualTo("Fluid,Cloth"));
        Assert.That(clone.CacheFormat, Is.EqualTo("Alembic"));
        Assert.That(clone.ResolutionMax, Is.EqualTo(256));
    }

    #endregion

    #region MemoryPack Tests

    [Test]
    public void MemoryPackRoundtripTest()
    {
        var opts = new RenderBakeOptionsData
        {
            SimulationKinds = "Fluid",
            CacheFormat = "OpenVDB",
            ResolutionMax = 64
        };

        var clone = opts.MemoryPackClone();

        Assert.That(clone, Was.EqualTo(opts));
    }

    #endregion

    #region Tools

    private static RenderBakeOptionsData CreateDefault()
    {
        return new RenderBakeOptionsData();
    }

    #endregion
}
