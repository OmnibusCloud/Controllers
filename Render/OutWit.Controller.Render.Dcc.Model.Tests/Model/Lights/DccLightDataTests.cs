using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Lights;

[TestFixture]
public sealed class DccLightDataTests
{
    [Test]
    public void IsTest()
    {
        var lightA = DccModelTestData.CreateLight();
        var lightB = DccModelTestData.CreateLight();
        var lightC = DccModelTestData.CreateLight();
        lightC.Intensity = 10d;

        Assert.That(lightA.Is(lightB), Is.True);
        Assert.That(lightA.Is(lightC), Is.False);
        Assert.That(lightA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateLight();
        var clone = (DccLightData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.ColorKeyframes.Count, Is.EqualTo(original.ColorKeyframes.Count));
            Assert.That(clone.ColorKeyframes[0].Color.R, Is.EqualTo(original.ColorKeyframes[0].Color.R));
            Assert.That(clone.Intensity, Is.EqualTo(original.Intensity));
            Assert.That(clone.IntensityKeyframes.Count, Is.EqualTo(original.IntensityKeyframes.Count));
            Assert.That(clone.IntensityKeyframes[0].Value, Is.EqualTo(original.IntensityKeyframes[0].Value));
            Assert.That(clone.Range, Is.EqualTo(original.Range));
            Assert.That(clone.SpotAngleDegrees, Is.EqualTo(original.SpotAngleDegrees));
            Assert.That(clone.Color.R, Is.EqualTo(original.Color.R));
            Assert.That(clone.Color.G, Is.EqualTo(original.Color.G));
            Assert.That(clone.Color.B, Is.EqualTo(original.Color.B));
            Assert.That(clone.Color, Is.Not.SameAs(original.Color));
            Assert.That(clone.ColorKeyframes, Is.Not.SameAs(original.ColorKeyframes));
            Assert.That(clone.IntensityKeyframes, Is.Not.SameAs(original.IntensityKeyframes));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateLight();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.ColorKeyframes.Count, Is.EqualTo(original.ColorKeyframes.Count));
            Assert.That(clone.ColorKeyframes[0].Color.R, Is.EqualTo(original.ColorKeyframes[0].Color.R));
            Assert.That(clone.Intensity, Is.EqualTo(original.Intensity));
            Assert.That(clone.IntensityKeyframes.Count, Is.EqualTo(original.IntensityKeyframes.Count));
            Assert.That(clone.IntensityKeyframes[0].Value, Is.EqualTo(original.IntensityKeyframes[0].Value));
            Assert.That(clone.Range, Is.EqualTo(original.Range));
            Assert.That(clone.SpotAngleDegrees, Is.EqualTo(original.SpotAngleDegrees));
            Assert.That(clone.Color.R, Is.EqualTo(original.Color.R));
            Assert.That(clone.Color.G, Is.EqualTo(original.Color.G));
            Assert.That(clone.Color.B, Is.EqualTo(original.Color.B));
            Assert.That(clone.Color, Is.Not.SameAs(original.Color));
            Assert.That(clone.ColorKeyframes, Is.Not.SameAs(original.ColorKeyframes));
            Assert.That(clone.IntensityKeyframes, Is.Not.SameAs(original.IntensityKeyframes));
        });
    }

    [Test]
    public void SpotLightCloneTest()
    {
        var original = DccModelTestData.CreateSpotLight();
        var clone = (DccLightData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Kind, Is.EqualTo(DccLightKind.Spot));
            Assert.That(clone.SpotAngleDegrees, Is.EqualTo(original.SpotAngleDegrees));
            Assert.That(clone.SpotAngleKeyframes.Count, Is.EqualTo(original.SpotAngleKeyframes.Count));
            Assert.That(clone.SpotAngleKeyframes[0].Value, Is.EqualTo(original.SpotAngleKeyframes[0].Value));
            Assert.That(clone.SpotAngleKeyframes, Is.Not.SameAs(original.SpotAngleKeyframes));
        });
    }

    [Test]
    public void PointLightCloneTest()
    {
        var original = DccModelTestData.CreatePointLight();
        var clone = (DccLightData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Kind, Is.EqualTo(DccLightKind.Point));
            Assert.That(clone.Range, Is.EqualTo(original.Range));
            Assert.That(clone.RangeKeyframes.Count, Is.EqualTo(original.RangeKeyframes.Count));
            Assert.That(clone.RangeKeyframes[0].Value, Is.EqualTo(original.RangeKeyframes[0].Value));
            Assert.That(clone.RangeKeyframes, Is.Not.SameAs(original.RangeKeyframes));
        });
    }
}
