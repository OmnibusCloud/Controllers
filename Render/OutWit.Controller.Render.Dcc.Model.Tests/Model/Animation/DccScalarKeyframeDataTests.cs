using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Animation;

[TestFixture]
public sealed class DccScalarKeyframeDataTests
{
    [Test]
    public void IsTest()
    {
        var keyframeA = DccModelTestData.CreateScalarKeyframe();
        var keyframeB = DccModelTestData.CreateScalarKeyframe();
        var keyframeC = DccModelTestData.CreateScalarKeyframe();
        keyframeC.InterpolationMode = DccKeyframeInterpolationMode.Linear;

        Assert.That(keyframeA.Is(keyframeB), Is.True);
        Assert.That(keyframeA.Is(keyframeC), Is.False);
        Assert.That(keyframeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateScalarKeyframe();
        var clone = (DccScalarKeyframeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.Value, Is.EqualTo(original.Value));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateScalarKeyframe();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Frame, Is.EqualTo(original.Frame));
            Assert.That(clone.Value, Is.EqualTo(original.Value));
            Assert.That(clone.InterpolationMode, Is.EqualTo(original.InterpolationMode));
        });
    }
}
