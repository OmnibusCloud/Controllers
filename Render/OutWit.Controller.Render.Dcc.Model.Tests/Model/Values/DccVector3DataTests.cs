using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Values;

[TestFixture]
public sealed class DccVector3DataTests
{
    [Test]
    public void IsTest()
    {
        var vectorA = DccModelTestData.CreateVector3();
        var vectorB = DccModelTestData.CreateVector3();
        var vectorC = DccModelTestData.CreateVector3();
        vectorC.Z = 4d;

        Assert.That(vectorA.Is(vectorB), Is.True);
        Assert.That(vectorA.Is(vectorC), Is.False);
        Assert.That(vectorA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateVector3();
        var clone = (DccVector3Data)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.X, Is.EqualTo(original.X));
            Assert.That(clone.Y, Is.EqualTo(original.Y));
            Assert.That(clone.Z, Is.EqualTo(original.Z));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateVector3();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.X, Is.EqualTo(original.X));
            Assert.That(clone.Y, Is.EqualTo(original.Y));
            Assert.That(clone.Z, Is.EqualTo(original.Z));
        });
    }
}
