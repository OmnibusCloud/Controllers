using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Cameras;

[TestFixture]
public sealed class DccCameraDataTests
{
    [Test]
    public void IsTest()
    {
        var cameraA = DccModelTestData.CreateCamera();
        var cameraB = DccModelTestData.CreateCamera();
        var cameraC = DccModelTestData.CreateCamera();
        cameraC.VerticalFovDegrees = 60d;

        Assert.That(cameraA.Is(cameraB), Is.True);
        Assert.That(cameraA.Is(cameraC), Is.False);
        Assert.That(cameraA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = (DccCameraData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.VerticalFovDegrees, Is.EqualTo(original.VerticalFovDegrees));
            Assert.That(clone.VerticalFovKeyframes.Count, Is.EqualTo(original.VerticalFovKeyframes.Count));
            Assert.That(clone.VerticalFovKeyframes[0].Value, Is.EqualTo(original.VerticalFovKeyframes[0].Value));
            Assert.That(clone.NearClip, Is.EqualTo(original.NearClip));
            Assert.That(clone.NearClipKeyframes.Count, Is.EqualTo(original.NearClipKeyframes.Count));
            Assert.That(clone.NearClipKeyframes[0].Value, Is.EqualTo(original.NearClipKeyframes[0].Value));
            Assert.That(clone.FarClip, Is.EqualTo(original.FarClip));
            Assert.That(clone.FarClipKeyframes.Count, Is.EqualTo(original.FarClipKeyframes.Count));
            Assert.That(clone.FarClipKeyframes[0].Value, Is.EqualTo(original.FarClipKeyframes[0].Value));
            Assert.That(clone.IsPerspective, Is.EqualTo(original.IsPerspective));
            Assert.That(clone.VerticalFovKeyframes, Is.Not.SameAs(original.VerticalFovKeyframes));
            Assert.That(clone.NearClipKeyframes, Is.Not.SameAs(original.NearClipKeyframes));
            Assert.That(clone.FarClipKeyframes, Is.Not.SameAs(original.FarClipKeyframes));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateCamera();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.VerticalFovDegrees, Is.EqualTo(original.VerticalFovDegrees));
            Assert.That(clone.VerticalFovKeyframes.Count, Is.EqualTo(original.VerticalFovKeyframes.Count));
            Assert.That(clone.VerticalFovKeyframes[0].Value, Is.EqualTo(original.VerticalFovKeyframes[0].Value));
            Assert.That(clone.NearClip, Is.EqualTo(original.NearClip));
            Assert.That(clone.NearClipKeyframes.Count, Is.EqualTo(original.NearClipKeyframes.Count));
            Assert.That(clone.NearClipKeyframes[0].Value, Is.EqualTo(original.NearClipKeyframes[0].Value));
            Assert.That(clone.FarClip, Is.EqualTo(original.FarClip));
            Assert.That(clone.FarClipKeyframes.Count, Is.EqualTo(original.FarClipKeyframes.Count));
            Assert.That(clone.FarClipKeyframes[0].Value, Is.EqualTo(original.FarClipKeyframes[0].Value));
            Assert.That(clone.IsPerspective, Is.EqualTo(original.IsPerspective));
            Assert.That(clone.VerticalFovKeyframes, Is.Not.SameAs(original.VerticalFovKeyframes));
            Assert.That(clone.NearClipKeyframes, Is.Not.SameAs(original.NearClipKeyframes));
            Assert.That(clone.FarClipKeyframes, Is.Not.SameAs(original.FarClipKeyframes));
        });
    }
}
