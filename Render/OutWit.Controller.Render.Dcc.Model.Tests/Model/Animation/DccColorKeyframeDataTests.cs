using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Animation;

[TestFixture]
public sealed class DccColorKeyframeDataTests
{
    [Test]
    public void IsTest()
    {
        var keyframeA = DccModelTestData.CreateColorKeyframe();
        var keyframeB = DccModelTestData.CreateColorKeyframe();
        var keyframeC = DccModelTestData.CreateColorKeyframe();
        keyframeC.InterpolationMode = DccKeyframeInterpolationMode.Linear;

        Assert.That(keyframeA.Is(keyframeB), Is.True);
        Assert.That(keyframeA.Is(keyframeC), Is.False);
        Assert.That(keyframeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateColorKeyframe();
        var clone = (DccColorKeyframeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
            Assert.That(clone.Color.R, Is.EqualTo(original.Color.R));
            Assert.That(clone.Color.G, Is.EqualTo(original.Color.G));
            Assert.That(clone.Color.B, Is.EqualTo(original.Color.B));
            Assert.That(clone.Color, Is.Not.SameAs(original.Color));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateColorKeyframe();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
            Assert.That(clone.Color.R, Is.EqualTo(original.Color.R));
            Assert.That(clone.Color.G, Is.EqualTo(original.Color.G));
            Assert.That(clone.Color.B, Is.EqualTo(original.Color.B));
            Assert.That(clone.Color, Is.Not.SameAs(original.Color));
        });
    }
}
