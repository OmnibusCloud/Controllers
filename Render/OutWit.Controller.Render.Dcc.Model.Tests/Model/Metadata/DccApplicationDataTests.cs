using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Metadata;

[TestFixture]
public sealed class DccApplicationDataTests
{
    [Test]
    public void IsTest()
    {
        var applicationA = DccModelTestData.CreateApplication();
        var applicationB = DccModelTestData.CreateApplication();
        var applicationC = DccModelTestData.CreateApplication();
        applicationC.ExporterVersion = "2.0.0";

        Assert.That(applicationA.Is(applicationB), Is.True);
        Assert.That(applicationA.Is(applicationC), Is.False);
        Assert.That(applicationA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateApplication();
        var clone = (DccApplicationData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ApplicationFamily, Is.EqualTo(original.ApplicationFamily));
            Assert.That(clone.ApplicationVersion, Is.EqualTo(original.ApplicationVersion));
            Assert.That(clone.ExporterVersion, Is.EqualTo(original.ExporterVersion));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateApplication();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ApplicationFamily, Is.EqualTo(original.ApplicationFamily));
            Assert.That(clone.ApplicationVersion, Is.EqualTo(original.ApplicationVersion));
            Assert.That(clone.ExporterVersion, Is.EqualTo(original.ExporterVersion));
        });
    }
}
