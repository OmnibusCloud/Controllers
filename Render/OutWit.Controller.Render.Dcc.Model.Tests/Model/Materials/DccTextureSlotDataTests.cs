using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Materials;

[TestFixture]
public sealed class DccTextureSlotDataTests
{
    [Test]
    public void IsTest()
    {
        var slotA = DccModelTestData.CreateTextureSlot();
        var slotB = DccModelTestData.CreateTextureSlot();
        var slotC = DccModelTestData.CreateTextureSlot();
        slotC.UvOffsetY = 0.25d;

        Assert.That(slotA.Is(slotB), Is.True);
        Assert.That(slotA.Is(slotC), Is.False);
        Assert.That(slotA.Is(null!), Is.False);
    }

    [Test]
    public void IsReturnsFalseWhenUvRotationDiffersTest()
    {
        var slotA = DccModelTestData.CreateTextureSlot();
        var slotB = DccModelTestData.CreateTextureSlot();
        slotB.UvRotationDegrees = 45d;

        Assert.That(slotA.Is(slotB), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateTextureSlot();
        var clone = (DccTextureSlotData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Slot, Is.EqualTo(original.Slot));
            Assert.That(clone.ImageAssetId, Is.EqualTo(original.ImageAssetId));
            Assert.That(clone.UvScaleX, Is.EqualTo(original.UvScaleX));
            Assert.That(clone.UvScaleY, Is.EqualTo(original.UvScaleY));
            Assert.That(clone.UvOffsetX, Is.EqualTo(original.UvOffsetX));
            Assert.That(clone.UvOffsetY, Is.EqualTo(original.UvOffsetY));
            Assert.That(clone.UvRotationDegrees, Is.EqualTo(original.UvRotationDegrees));
            Assert.That(clone.UvTransformKeyframes.Count, Is.EqualTo(original.UvTransformKeyframes.Count));
            Assert.That(clone.UvTransformKeyframes[0].UvScaleX, Is.EqualTo(original.UvTransformKeyframes[0].UvScaleX));
            Assert.That(clone.UvTransformKeyframes, Is.Not.SameAs(original.UvTransformKeyframes));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateTextureSlot();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Slot, Is.EqualTo(original.Slot));
            Assert.That(clone.ImageAssetId, Is.EqualTo(original.ImageAssetId));
            Assert.That(clone.UvScaleX, Is.EqualTo(original.UvScaleX));
            Assert.That(clone.UvScaleY, Is.EqualTo(original.UvScaleY));
            Assert.That(clone.UvOffsetX, Is.EqualTo(original.UvOffsetX));
            Assert.That(clone.UvOffsetY, Is.EqualTo(original.UvOffsetY));
            Assert.That(clone.UvRotationDegrees, Is.EqualTo(original.UvRotationDegrees));
            Assert.That(clone.UvTransformKeyframes.Count, Is.EqualTo(original.UvTransformKeyframes.Count));
            Assert.That(clone.UvTransformKeyframes[0].UvScaleX, Is.EqualTo(original.UvTransformKeyframes[0].UvScaleX));
            Assert.That(clone.UvTransformKeyframes, Is.Not.SameAs(original.UvTransformKeyframes));
        });
    }
}
