using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Animation;

[TestFixture]
public sealed class DccTextureTransformKeyframeDataTests
{
    [Test]
    public void IsTest()
    {
        var keyframeA = DccModelTestData.CreateTextureTransformKeyframe();
        var keyframeB = DccModelTestData.CreateTextureTransformKeyframe();
        var keyframeC = DccModelTestData.CreateTextureTransformKeyframe();
        keyframeC.UvRotationDegrees = 45d;

        Assert.That(keyframeA.Is(keyframeB), Is.True);
        Assert.That(keyframeA.Is(keyframeC), Is.False);
        Assert.That(keyframeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateTextureTransformKeyframe();
        var clone = (DccTextureTransformKeyframeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.UvScaleX, Is.EqualTo(original.UvScaleX));
            Assert.That(clone.UvScaleY, Is.EqualTo(original.UvScaleY));
            Assert.That(clone.UvOffsetX, Is.EqualTo(original.UvOffsetX));
            Assert.That(clone.UvOffsetY, Is.EqualTo(original.UvOffsetY));
            Assert.That(clone.UvRotationDegrees, Is.EqualTo(original.UvRotationDegrees));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateTextureTransformKeyframe();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.UvScaleX, Is.EqualTo(original.UvScaleX));
            Assert.That(clone.UvScaleY, Is.EqualTo(original.UvScaleY));
            Assert.That(clone.UvOffsetX, Is.EqualTo(original.UvOffsetX));
            Assert.That(clone.UvOffsetY, Is.EqualTo(original.UvOffsetY));
            Assert.That(clone.UvRotationDegrees, Is.EqualTo(original.UvRotationDegrees));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }
}
