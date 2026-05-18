using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model;

[TestFixture]
public sealed class DccMaterialDataTests
{
    [Test]
    public void IsTest()
    {
        var materialA = DccModelTestData.CreateMaterial();
        var materialB = DccModelTestData.CreateMaterial();
        var materialC = DccModelTestData.CreateMaterial();
        materialC.Roughness = 0.2d;

        Assert.That(materialA.Is(materialB), Is.True);
        Assert.That(materialA.Is(materialC), Is.False);
        Assert.That(materialA.Is(null!), Is.False);
    }

    [Test]
    public void IsReturnsFalseWhenNormalStrengthDiffersTest()
    {
        var materialA = DccModelTestData.CreateMaterial();
        var materialB = DccModelTestData.CreateMaterial();
        materialB.NormalStrength = 2d;

        Assert.That(materialA.Is(materialB), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateMaterial();
        var clone = (DccMaterialData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.BaseColorKeyframes.Count, Is.EqualTo(original.BaseColorKeyframes.Count));
            Assert.That(clone.BaseColorKeyframes[0].Color.R, Is.EqualTo(original.BaseColorKeyframes[0].Color.R));
            Assert.That(clone.AlphaMode, Is.EqualTo(original.AlphaMode));
            Assert.That(clone.AlphaClipThreshold, Is.EqualTo(original.AlphaClipThreshold));
            Assert.That(clone.AlphaClipThresholdKeyframes.Count, Is.EqualTo(original.AlphaClipThresholdKeyframes.Count));
            Assert.That(clone.AlphaClipThresholdKeyframes[0].Value, Is.EqualTo(original.AlphaClipThresholdKeyframes[0].Value));
            Assert.That(clone.Opacity, Is.EqualTo(original.Opacity));
            Assert.That(clone.OpacityKeyframes.Count, Is.EqualTo(original.OpacityKeyframes.Count));
            Assert.That(clone.OpacityKeyframes[0].Value, Is.EqualTo(original.OpacityKeyframes[0].Value));
            Assert.That(clone.Metallic, Is.EqualTo(original.Metallic));
            Assert.That(clone.MetallicKeyframes.Count, Is.EqualTo(original.MetallicKeyframes.Count));
            Assert.That(clone.MetallicKeyframes[0].Value, Is.EqualTo(original.MetallicKeyframes[0].Value));
            Assert.That(clone.Roughness, Is.EqualTo(original.Roughness));
            Assert.That(clone.RoughnessKeyframes.Count, Is.EqualTo(original.RoughnessKeyframes.Count));
            Assert.That(clone.RoughnessKeyframes[0].Value, Is.EqualTo(original.RoughnessKeyframes[0].Value));
            Assert.That(clone.NormalStrength, Is.EqualTo(original.NormalStrength));
            Assert.That(clone.NormalStrengthKeyframes.Count, Is.EqualTo(original.NormalStrengthKeyframes.Count));
            Assert.That(clone.NormalStrengthKeyframes[0].Value, Is.EqualTo(original.NormalStrengthKeyframes[0].Value));
            Assert.That(clone.BaseColor.R, Is.EqualTo(original.BaseColor.R));
            Assert.That(clone.BaseColor.G, Is.EqualTo(original.BaseColor.G));
            Assert.That(clone.BaseColor.B, Is.EqualTo(original.BaseColor.B));
            Assert.That(clone.TextureSlots[0].Slot, Is.EqualTo(original.TextureSlots[0].Slot));
            Assert.That(clone.TextureSlots[0].ImageAssetId, Is.EqualTo(original.TextureSlots[0].ImageAssetId));
            Assert.That(clone.BaseColor, Is.Not.SameAs(original.BaseColor));
            Assert.That(clone.BaseColorKeyframes, Is.Not.SameAs(original.BaseColorKeyframes));
            Assert.That(clone.AlphaClipThresholdKeyframes, Is.Not.SameAs(original.AlphaClipThresholdKeyframes));
            Assert.That(clone.OpacityKeyframes, Is.Not.SameAs(original.OpacityKeyframes));
            Assert.That(clone.MetallicKeyframes, Is.Not.SameAs(original.MetallicKeyframes));
            Assert.That(clone.RoughnessKeyframes, Is.Not.SameAs(original.RoughnessKeyframes));
            Assert.That(clone.NormalStrengthKeyframes, Is.Not.SameAs(original.NormalStrengthKeyframes));
            Assert.That(clone.TextureSlots, Is.Not.SameAs(original.TextureSlots));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateMaterial();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.BaseColorKeyframes.Count, Is.EqualTo(original.BaseColorKeyframes.Count));
            Assert.That(clone.BaseColorKeyframes[0].Color.R, Is.EqualTo(original.BaseColorKeyframes[0].Color.R));
            Assert.That(clone.AlphaMode, Is.EqualTo(original.AlphaMode));
            Assert.That(clone.AlphaClipThreshold, Is.EqualTo(original.AlphaClipThreshold));
            Assert.That(clone.AlphaClipThresholdKeyframes.Count, Is.EqualTo(original.AlphaClipThresholdKeyframes.Count));
            Assert.That(clone.AlphaClipThresholdKeyframes[0].Value, Is.EqualTo(original.AlphaClipThresholdKeyframes[0].Value));
            Assert.That(clone.Opacity, Is.EqualTo(original.Opacity));
            Assert.That(clone.OpacityKeyframes.Count, Is.EqualTo(original.OpacityKeyframes.Count));
            Assert.That(clone.OpacityKeyframes[0].Value, Is.EqualTo(original.OpacityKeyframes[0].Value));
            Assert.That(clone.Metallic, Is.EqualTo(original.Metallic));
            Assert.That(clone.MetallicKeyframes.Count, Is.EqualTo(original.MetallicKeyframes.Count));
            Assert.That(clone.MetallicKeyframes[0].Value, Is.EqualTo(original.MetallicKeyframes[0].Value));
            Assert.That(clone.Roughness, Is.EqualTo(original.Roughness));
            Assert.That(clone.RoughnessKeyframes.Count, Is.EqualTo(original.RoughnessKeyframes.Count));
            Assert.That(clone.RoughnessKeyframes[0].Value, Is.EqualTo(original.RoughnessKeyframes[0].Value));
            Assert.That(clone.NormalStrength, Is.EqualTo(original.NormalStrength));
            Assert.That(clone.NormalStrengthKeyframes.Count, Is.EqualTo(original.NormalStrengthKeyframes.Count));
            Assert.That(clone.NormalStrengthKeyframes[0].Value, Is.EqualTo(original.NormalStrengthKeyframes[0].Value));
            Assert.That(clone.BaseColor.R, Is.EqualTo(original.BaseColor.R));
            Assert.That(clone.BaseColor.G, Is.EqualTo(original.BaseColor.G));
            Assert.That(clone.BaseColor.B, Is.EqualTo(original.BaseColor.B));
            Assert.That(clone.TextureSlots[0].Slot, Is.EqualTo(original.TextureSlots[0].Slot));
            Assert.That(clone.TextureSlots[0].ImageAssetId, Is.EqualTo(original.TextureSlots[0].ImageAssetId));
            Assert.That(clone.BaseColor, Is.Not.SameAs(original.BaseColor));
            Assert.That(clone.BaseColorKeyframes, Is.Not.SameAs(original.BaseColorKeyframes));
            Assert.That(clone.AlphaClipThresholdKeyframes, Is.Not.SameAs(original.AlphaClipThresholdKeyframes));
            Assert.That(clone.OpacityKeyframes, Is.Not.SameAs(original.OpacityKeyframes));
            Assert.That(clone.MetallicKeyframes, Is.Not.SameAs(original.MetallicKeyframes));
            Assert.That(clone.RoughnessKeyframes, Is.Not.SameAs(original.RoughnessKeyframes));
            Assert.That(clone.NormalStrengthKeyframes, Is.Not.SameAs(original.NormalStrengthKeyframes));
            Assert.That(clone.TextureSlots, Is.Not.SameAs(original.TextureSlots));
        });
    }
}
