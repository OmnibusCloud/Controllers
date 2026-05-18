using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Values;

[TestFixture]
public sealed class DccQuaternionDataTests
{
    [Test]
    public void IsTest()
    {
        var quaternionA = DccModelTestData.CreateQuaternion();
        var quaternionB = DccModelTestData.CreateQuaternion();
        var quaternionC = DccModelTestData.CreateQuaternion();
        quaternionC.W = 1d;

        Assert.That(quaternionA.Is(quaternionB), Is.True);
        Assert.That(quaternionA.Is(quaternionC), Is.False);
        Assert.That(quaternionA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateQuaternion();
        var clone = (DccQuaternionData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.X, Is.EqualTo(original.X));
            Assert.That(clone.Y, Is.EqualTo(original.Y));
            Assert.That(clone.Z, Is.EqualTo(original.Z));
            Assert.That(clone.W, Is.EqualTo(original.W));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateQuaternion();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.X, Is.EqualTo(original.X));
            Assert.That(clone.Y, Is.EqualTo(original.Y));
            Assert.That(clone.Z, Is.EqualTo(original.Z));
            Assert.That(clone.W, Is.EqualTo(original.W));
        });
    }
}
