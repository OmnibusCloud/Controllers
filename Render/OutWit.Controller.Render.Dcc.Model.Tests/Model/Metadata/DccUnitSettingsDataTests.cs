using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Metadata;

[TestFixture]
public sealed class DccUnitSettingsDataTests
{
    [Test]
    public void IsTest()
    {
        var unitsA = DccModelTestData.CreateUnitSettings();
        var unitsB = DccModelTestData.CreateUnitSettings();
        var unitsC = DccModelTestData.CreateUnitSettings();
        unitsC.UnitsPerMeter = 1d;

        Assert.That(unitsA.Is(unitsB), Is.True);
        Assert.That(unitsA.Is(unitsC), Is.False);
        Assert.That(unitsA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateUnitSettings();
        var clone = (DccUnitSettingsData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.LinearUnit, Is.EqualTo(original.LinearUnit));
            Assert.That(clone.UnitsPerMeter, Is.EqualTo(original.UnitsPerMeter));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateUnitSettings();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.LinearUnit, Is.EqualTo(original.LinearUnit));
            Assert.That(clone.UnitsPerMeter, Is.EqualTo(original.UnitsPerMeter));
        });
    }
}
