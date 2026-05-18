using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Materials;

[TestFixture]
public sealed class DccImageAssetDataTests
{
    [Test]
    public void IsTest()
    {
        var assetA = DccModelTestData.CreateImageAsset();
        var assetB = DccModelTestData.CreateImageAsset();
        var assetC = DccModelTestData.CreateImageAsset();
        assetC.RelativePath = "textures/other.png";

        Assert.That(assetA.Is(assetB), Is.True);
        Assert.That(assetA.Is(assetC), Is.False);
        Assert.That(assetA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateImageAsset();
        var clone = (DccImageAssetData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.SourcePath, Is.EqualTo(original.SourcePath));
            Assert.That(clone.RelativePath, Is.EqualTo(original.RelativePath));
            Assert.That(clone.AssetKind, Is.EqualTo(original.AssetKind));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateImageAsset();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.SourcePath, Is.EqualTo(original.SourcePath));
            Assert.That(clone.RelativePath, Is.EqualTo(original.RelativePath));
            Assert.That(clone.AssetKind, Is.EqualTo(original.AssetKind));
        });
    }
}
