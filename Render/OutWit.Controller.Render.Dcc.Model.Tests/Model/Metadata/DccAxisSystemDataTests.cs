using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Metadata;

[TestFixture]
public sealed class DccAxisSystemDataTests
{
    [Test]
    public void IsTest()
    {
        var axisA = DccModelTestData.CreateAxisSystem();
        var axisB = DccModelTestData.CreateAxisSystem();
        var axisC = DccModelTestData.CreateAxisSystem();
        axisC.ForwardAxis = "X";

        Assert.That(axisA.Is(axisB), Is.True);
        Assert.That(axisA.Is(axisC), Is.False);
        Assert.That(axisA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateAxisSystem();
        var clone = (DccAxisSystemData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Handedness, Is.EqualTo(original.Handedness));
            Assert.That(clone.UpAxis, Is.EqualTo(original.UpAxis));
            Assert.That(clone.ForwardAxis, Is.EqualTo(original.ForwardAxis));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateAxisSystem();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Handedness, Is.EqualTo(original.Handedness));
            Assert.That(clone.UpAxis, Is.EqualTo(original.UpAxis));
            Assert.That(clone.ForwardAxis, Is.EqualTo(original.ForwardAxis));
        });
    }
}
