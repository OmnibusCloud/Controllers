using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Materials;

[TestFixture]
public sealed class DccMaterialDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForTextureSlotsAndBaseColor()
    {
        var original = DccModelTestData.CreateMaterial();
        var clone = (DccMaterialData)original.Clone();

        clone.BaseColor.R = 0.1d;
        clone.BaseColorKeyframes[0].Color.R = 0.1d;
        clone.AlphaClipThresholdKeyframes[0].Value = 0.2d;
        clone.OpacityKeyframes[0].Value = 0.5d;
        clone.MetallicKeyframes[0].Value = 0.8d;
        clone.RoughnessKeyframes[0].Value = 0.2d;
        clone.NormalStrengthKeyframes[0].Value = 2d;
        clone.TextureSlots[0].UvScaleX = 2d;

        Assert.Multiple(() =>
        {
            Assert.That(original.BaseColor.R, Is.EqualTo(0.8d));
            Assert.That(original.BaseColorKeyframes[0].Color.R, Is.EqualTo(0.8d));
            Assert.That(original.AlphaClipThresholdKeyframes[0].Value, Is.EqualTo(0.5d));
            Assert.That(original.OpacityKeyframes[0].Value, Is.EqualTo(1d));
            Assert.That(original.MetallicKeyframes[0].Value, Is.EqualTo(0.1d));
            Assert.That(original.RoughnessKeyframes[0].Value, Is.EqualTo(0.5d));
            Assert.That(original.NormalStrengthKeyframes[0].Value, Is.EqualTo(1d));
            Assert.That(original.TextureSlots[0].UvScaleX, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForTextureSlotsAndBaseColor()
    {
        var original = DccModelTestData.CreateMaterial();
        var clone = original.MemoryPackClone();

        clone.BaseColor.R = 0.1d;
        clone.BaseColorKeyframes[0].Color.R = 0.1d;
        clone.AlphaClipThresholdKeyframes[0].Value = 0.2d;
        clone.OpacityKeyframes[0].Value = 0.5d;
        clone.MetallicKeyframes[0].Value = 0.8d;
        clone.RoughnessKeyframes[0].Value = 0.2d;
        clone.NormalStrengthKeyframes[0].Value = 2d;
        clone.TextureSlots[0].UvScaleX = 2d;

        Assert.Multiple(() =>
        {
            Assert.That(original.BaseColor.R, Is.EqualTo(0.8d));
            Assert.That(original.BaseColorKeyframes[0].Color.R, Is.EqualTo(0.8d));
            Assert.That(original.AlphaClipThresholdKeyframes[0].Value, Is.EqualTo(0.5d));
            Assert.That(original.OpacityKeyframes[0].Value, Is.EqualTo(1d));
            Assert.That(original.MetallicKeyframes[0].Value, Is.EqualTo(0.1d));
            Assert.That(original.RoughnessKeyframes[0].Value, Is.EqualTo(0.5d));
            Assert.That(original.NormalStrengthKeyframes[0].Value, Is.EqualTo(1d));
            Assert.That(original.TextureSlots[0].UvScaleX, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
