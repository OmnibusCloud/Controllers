using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Materials;

[TestFixture]
public sealed class DccTextureSlotDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForUvTransformKeyframes()
    {
        var original = DccModelTestData.CreateTextureSlot();
        var clone = (DccTextureSlotData)original.Clone();

        clone.UvTransformKeyframes[0].UvOffsetX = 0.25d;

        Assert.Multiple(() =>
        {
            Assert.That(original.UvTransformKeyframes[0].UvOffsetX, Is.EqualTo(0d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForUvTransformKeyframes()
    {
        var original = DccModelTestData.CreateTextureSlot();
        var clone = original.MemoryPackClone();

        clone.UvTransformKeyframes[0].UvOffsetX = 0.25d;

        Assert.Multiple(() =>
        {
            Assert.That(original.UvTransformKeyframes[0].UvOffsetX, Is.EqualTo(0d));
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
