using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Values;

[TestFixture]
public sealed class DccColorDataTests
{
    [Test]
    public void IsTest()
    {
        var colorA = DccModelTestData.CreateColor();
        var colorB = DccModelTestData.CreateColor();
        var colorC = DccModelTestData.CreateColor();
        colorC.B = 0.1d;

        Assert.That(colorA.Is(colorB), Is.True);
        Assert.That(colorA.Is(colorC), Is.False);
        Assert.That(colorA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateColor();
        var clone = (DccColorData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.R, Is.EqualTo(original.R));
            Assert.That(clone.G, Is.EqualTo(original.G));
            Assert.That(clone.B, Is.EqualTo(original.B));
            Assert.That(clone.A, Is.EqualTo(original.A));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateColor();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.R, Is.EqualTo(original.R));
            Assert.That(clone.G, Is.EqualTo(original.G));
            Assert.That(clone.B, Is.EqualTo(original.B));
            Assert.That(clone.A, Is.EqualTo(original.A));
        });
    }
}
