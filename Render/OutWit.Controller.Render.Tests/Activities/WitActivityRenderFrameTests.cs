using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Render.Activities;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public class WitActivityRenderFrameTests
{
    #region Clone Tests

    [Test]
    public void ClonePreservesPropertiesTest()
    {
        var activity = new WitActivityRenderFrame();

        var clone = activity.Clone();

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone, Is.InstanceOf<WitActivityRenderFrame>());
    }

    #endregion

    #region ToString Tests

    [Test]
    public void ToStringContainsActivityNameTest()
    {
        var activity = new WitActivityRenderFrame();
        var str = activity.ToString();

        Assert.That(str, Does.Contain("Render.Frame"));
    }

    #endregion

    #region Attribute Tests

    [Test]
    public void HasActivityAttributeTest()
    {
        var attrs = typeof(WitActivityRenderFrame)
            .GetCustomAttributes(typeof(Engine.Data.Attributes.ActivityAttribute), false);

        Assert.That(attrs, Has.Length.EqualTo(1));
    }

    [Test]
    public void HasMemoryPackableAttributeTest()
    {
        var attrs = typeof(WitActivityRenderFrame)
            .GetCustomAttributes(typeof(MemoryPack.MemoryPackableAttribute), false);

        Assert.That(attrs, Has.Length.EqualTo(1));
    }

    #endregion
}
