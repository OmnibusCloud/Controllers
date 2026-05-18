using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Animation;

[TestFixture]
public sealed class DccTransformKeyframeDataTests
{
    [Test]
    public void IsTest()
    {
        var keyframeA = DccModelTestData.CreateTransformKeyframe();
        var keyframeB = DccModelTestData.CreateTransformKeyframe();
        var keyframeC = DccModelTestData.CreateTransformKeyframe();
        keyframeC.InterpolationMode = DccKeyframeInterpolationMode.Linear;

        Assert.That(keyframeA.Is(keyframeB), Is.True);
        Assert.That(keyframeA.Is(keyframeC), Is.False);
        Assert.That(keyframeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateTransformKeyframe();
        var clone = (DccTransformKeyframeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
            Assert.That(clone.Transform.Translation.X, Is.EqualTo(original.Transform.Translation.X));
            Assert.That(clone.Transform, Is.Not.SameAs(original.Transform));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateTransformKeyframe();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
            Assert.That(clone.Transform.Translation.X, Is.EqualTo(original.Transform.Translation.X));
            Assert.That(clone.Transform, Is.Not.SameAs(original.Transform));
        });
    }
}
