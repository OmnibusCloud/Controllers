using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Cameras;

[TestFixture]
public sealed class DccCameraDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForVerticalFovKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = (DccCameraData)original.Clone();

        clone.VerticalFovKeyframes[0].Value = 60d;

        Assert.Multiple(() =>
        {
            Assert.That(original.VerticalFovKeyframes[0].Value, Is.EqualTo(45d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForVerticalFovKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = original.MemoryPackClone();

        clone.VerticalFovKeyframes[0].Value = 60d;

        Assert.Multiple(() =>
        {
            Assert.That(original.VerticalFovKeyframes[0].Value, Is.EqualTo(45d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForNearClipKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = (DccCameraData)original.Clone();

        clone.NearClipKeyframes[0].Value = 0.5d;

        Assert.Multiple(() =>
        {
            Assert.That(original.NearClipKeyframes[0].Value, Is.EqualTo(0.1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForFarClipKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = (DccCameraData)original.Clone();

        clone.FarClipKeyframes[0].Value = 600d;

        Assert.Multiple(() =>
        {
            Assert.That(original.FarClipKeyframes[0].Value, Is.EqualTo(500d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForNearClipKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = original.MemoryPackClone();

        clone.NearClipKeyframes[0].Value = 0.5d;

        Assert.Multiple(() =>
        {
            Assert.That(original.NearClipKeyframes[0].Value, Is.EqualTo(0.1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForFarClipKeyframes()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = original.MemoryPackClone();

        clone.FarClipKeyframes[0].Value = 600d;

        Assert.Multiple(() =>
        {
            Assert.That(original.FarClipKeyframes[0].Value, Is.EqualTo(500d));
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
