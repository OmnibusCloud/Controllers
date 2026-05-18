using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Lights;

[TestFixture]
public sealed class DccLightDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForColorKeyframes()
    {
        var original = DccModelTestData.CreateLight();
        var clone = (DccLightData)original.Clone();

        clone.ColorKeyframes[0].Color.R = 0.2d;

        Assert.Multiple(() =>
        {
            Assert.That(original.ColorKeyframes[0].Color.R, Is.EqualTo(0.8d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForIntensityKeyframes()
    {
        var original = DccModelTestData.CreateLight();
        var clone = (DccLightData)original.Clone();

        clone.IntensityKeyframes[0].Value = 10d;

        Assert.Multiple(() =>
        {
            Assert.That(original.IntensityKeyframes[0].Value, Is.EqualTo(4d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForColorKeyframes()
    {
        var original = DccModelTestData.CreateLight();
        var clone = original.MemoryPackClone();

        clone.ColorKeyframes[0].Color.R = 0.2d;

        Assert.Multiple(() =>
        {
            Assert.That(original.ColorKeyframes[0].Color.R, Is.EqualTo(0.8d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForIntensityKeyframes()
    {
        var original = DccModelTestData.CreateLight();
        var clone = original.MemoryPackClone();

        clone.IntensityKeyframes[0].Value = 10d;

        Assert.Multiple(() =>
        {
            Assert.That(original.IntensityKeyframes[0].Value, Is.EqualTo(4d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForSpotAngleKeyframes()
    {
        var original = DccModelTestData.CreateSpotLight();
        var clone = (DccLightData)original.Clone();

        clone.SpotAngleKeyframes[0].Value = 20d;

        Assert.Multiple(() =>
        {
            Assert.That(original.SpotAngleKeyframes[0].Value, Is.EqualTo(35d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForSpotAngleKeyframes()
    {
        var original = DccModelTestData.CreateSpotLight();
        var clone = original.MemoryPackClone();

        clone.SpotAngleKeyframes[0].Value = 20d;

        Assert.Multiple(() =>
        {
            Assert.That(original.SpotAngleKeyframes[0].Value, Is.EqualTo(35d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForRangeKeyframes()
    {
        var original = DccModelTestData.CreatePointLight();
        var clone = (DccLightData)original.Clone();

        clone.RangeKeyframes[0].Value = 10d;

        Assert.Multiple(() =>
        {
            Assert.That(original.RangeKeyframes[0].Value, Is.EqualTo(20d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForRangeKeyframes()
    {
        var original = DccModelTestData.CreatePointLight();
        var clone = original.MemoryPackClone();

        clone.RangeKeyframes[0].Value = 10d;

        Assert.Multiple(() =>
        {
            Assert.That(original.RangeKeyframes[0].Value, Is.EqualTo(20d));
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
