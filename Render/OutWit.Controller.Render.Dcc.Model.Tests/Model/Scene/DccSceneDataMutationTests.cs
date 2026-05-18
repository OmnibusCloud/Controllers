using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Scene;

[TestFixture]
public sealed class DccSceneDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForNestedCollections()
    {
        var original = DccModelTestData.CreateScene();
        var clone = (DccSceneData)original.Clone();

        clone.Nodes[0].Name = "ChangedNode";
        clone.Meshes[0].Positions[0].X = 999d;
        clone.Materials[0].TextureSlots[0].UvOffsetX = 0.25d;
        clone.AttachedFiles[0].RelativePath = "changed/path.png";

        Assert.Multiple(() =>
        {
            Assert.That(original.Nodes[0].Name, Is.EqualTo("Cube"));
            Assert.That(original.Meshes[0].Positions[0].X, Is.EqualTo(-1d));
            Assert.That(original.Materials[0].TextureSlots[0].UvOffsetX, Is.EqualTo(0d));
            Assert.That(original.AttachedFiles[0].RelativePath, Is.EqualTo("textures/albedo.png"));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForNestedCollections()
    {
        var original = DccModelTestData.CreateScene();
        var clone = original.MemoryPackClone();

        clone.Nodes[0].Name = "ChangedNode";
        clone.Meshes[0].Positions[0].X = 999d;
        clone.Materials[0].TextureSlots[0].UvOffsetX = 0.25d;
        clone.AttachedFiles[0].RelativePath = "changed/path.png";

        Assert.Multiple(() =>
        {
            Assert.That(original.Nodes[0].Name, Is.EqualTo("Cube"));
            Assert.That(original.Meshes[0].Positions[0].X, Is.EqualTo(-1d));
            Assert.That(original.Materials[0].TextureSlots[0].UvOffsetX, Is.EqualTo(0d));
            Assert.That(original.AttachedFiles[0].RelativePath, Is.EqualTo("textures/albedo.png"));
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
