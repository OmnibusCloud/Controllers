using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Scene;

[TestFixture]
public sealed class DccTransformDataTests
{
    [Test]
    public void IsTest()
    {
        var transformA = DccModelTestData.CreateTransform();
        var transformB = DccModelTestData.CreateTransform();
        var transformC = DccModelTestData.CreateTransform();
        transformC.Scale.Z = 2d;

        Assert.That(transformA.Is(transformB), Is.True);
        Assert.That(transformA.Is(transformC), Is.False);
        Assert.That(transformA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateTransform();
        var clone = (DccTransformData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Translation.X, Is.EqualTo(original.Translation.X));
            Assert.That(clone.Translation.Y, Is.EqualTo(original.Translation.Y));
            Assert.That(clone.Translation.Z, Is.EqualTo(original.Translation.Z));
            Assert.That(clone.Rotation.W, Is.EqualTo(original.Rotation.W));
            Assert.That(clone.Scale.X, Is.EqualTo(original.Scale.X));
            Assert.That(clone.Scale.Y, Is.EqualTo(original.Scale.Y));
            Assert.That(clone.Scale.Z, Is.EqualTo(original.Scale.Z));
            Assert.That(clone.Translation, Is.Not.SameAs(original.Translation));
            Assert.That(clone.Rotation, Is.Not.SameAs(original.Rotation));
            Assert.That(clone.Scale, Is.Not.SameAs(original.Scale));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateTransform();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Translation.X, Is.EqualTo(original.Translation.X));
            Assert.That(clone.Translation.Y, Is.EqualTo(original.Translation.Y));
            Assert.That(clone.Translation.Z, Is.EqualTo(original.Translation.Z));
            Assert.That(clone.Rotation.W, Is.EqualTo(original.Rotation.W));
            Assert.That(clone.Scale.X, Is.EqualTo(original.Scale.X));
            Assert.That(clone.Scale.Y, Is.EqualTo(original.Scale.Y));
            Assert.That(clone.Scale.Z, Is.EqualTo(original.Scale.Z));
            Assert.That(clone.Translation, Is.Not.SameAs(original.Translation));
            Assert.That(clone.Rotation, Is.Not.SameAs(original.Rotation));
            Assert.That(clone.Scale, Is.Not.SameAs(original.Scale));
        });
    }
}
