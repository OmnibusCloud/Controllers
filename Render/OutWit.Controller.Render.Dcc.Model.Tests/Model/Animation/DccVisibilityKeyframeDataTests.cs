using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Animation;

[TestFixture]
public sealed class DccVisibilityKeyframeDataTests
{
    [Test]
    public void IsTest()
    {
        var keyframeA = DccModelTestData.CreateVisibilityKeyframe();
        var keyframeB = DccModelTestData.CreateVisibilityKeyframe();
        var keyframeC = DccModelTestData.CreateVisibilityKeyframe();
        keyframeC.InterpolationMode = DccKeyframeInterpolationMode.Constant;

        Assert.That(keyframeA.Is(keyframeB), Is.True);
        Assert.That(keyframeA.Is(keyframeC), Is.False);
        Assert.That(keyframeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateVisibilityKeyframe();
        var clone = (DccVisibilityKeyframeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.Visible, Is.EqualTo(original.Visible));
            Assert.That(clone.Renderable, Is.EqualTo(original.Renderable));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateVisibilityKeyframe();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.Visible, Is.EqualTo(original.Visible));
            Assert.That(clone.Renderable, Is.EqualTo(original.Renderable));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }
}
