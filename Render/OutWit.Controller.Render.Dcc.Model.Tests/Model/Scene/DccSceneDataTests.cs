using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model;

[TestFixture]
public sealed class DccSceneDataTests
{
    [Test]
    public void IsTest()
    {
        var sceneA = DccModelTestData.CreateScene();
        var sceneB = DccModelTestData.CreateScene();
        var sceneC = DccModelTestData.CreateScene();
        sceneC.Lights[0].Intensity = 8d;

        Assert.That(sceneA.Is(sceneB), Is.True);
        Assert.That(sceneA.Is(sceneC), Is.False);
        Assert.That(sceneA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateScene();
        var clone = (DccSceneData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.SceneName, Is.EqualTo(original.SceneName));
            Assert.That(clone.SourceApplication.ApplicationFamily, Is.EqualTo(original.SourceApplication.ApplicationFamily));
            Assert.That(clone.Units.LinearUnit, Is.EqualTo(original.Units.LinearUnit));
            Assert.That(clone.AxisSystem.UpAxis, Is.EqualTo(original.AxisSystem.UpAxis));
            Assert.That(clone.RenderSettings.TargetEngine, Is.EqualTo(original.RenderSettings.TargetEngine));
            Assert.That(clone.Nodes.Count, Is.EqualTo(original.Nodes.Count));
            Assert.That(clone.Meshes.Count, Is.EqualTo(original.Meshes.Count));
            Assert.That(clone.Cameras.Count, Is.EqualTo(original.Cameras.Count));
            Assert.That(clone.Lights.Count, Is.EqualTo(original.Lights.Count));
            Assert.That(clone.Materials.Count, Is.EqualTo(original.Materials.Count));
            Assert.That(clone.ImageAssets.Count, Is.EqualTo(original.ImageAssets.Count));
            Assert.That(clone.AttachedFiles.Count, Is.EqualTo(original.AttachedFiles.Count));
            Assert.That(clone.Nodes[0].Id, Is.EqualTo(original.Nodes[0].Id));
            Assert.That(clone.Meshes[0].Id, Is.EqualTo(original.Meshes[0].Id));
            Assert.That(clone.Materials[0].Id, Is.EqualTo(original.Materials[0].Id));
            Assert.That(clone.Nodes, Is.Not.SameAs(original.Nodes));
            Assert.That(clone.Meshes, Is.Not.SameAs(original.Meshes));
            Assert.That(clone.Cameras, Is.Not.SameAs(original.Cameras));
            Assert.That(clone.Lights, Is.Not.SameAs(original.Lights));
            Assert.That(clone.Materials, Is.Not.SameAs(original.Materials));
            Assert.That(clone.ImageAssets, Is.Not.SameAs(original.ImageAssets));
            Assert.That(clone.AttachedFiles, Is.Not.SameAs(original.AttachedFiles));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateScene();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.SceneName, Is.EqualTo(original.SceneName));
            Assert.That(clone.SourceApplication.ApplicationFamily, Is.EqualTo(original.SourceApplication.ApplicationFamily));
            Assert.That(clone.Units.LinearUnit, Is.EqualTo(original.Units.LinearUnit));
            Assert.That(clone.AxisSystem.UpAxis, Is.EqualTo(original.AxisSystem.UpAxis));
            Assert.That(clone.RenderSettings.TargetEngine, Is.EqualTo(original.RenderSettings.TargetEngine));
            Assert.That(clone.Nodes.Count, Is.EqualTo(original.Nodes.Count));
            Assert.That(clone.Meshes.Count, Is.EqualTo(original.Meshes.Count));
            Assert.That(clone.Materials.Count, Is.EqualTo(original.Materials.Count));
            Assert.That(clone.Nodes[0].Id, Is.EqualTo(original.Nodes[0].Id));
            Assert.That(clone.Meshes[0].Id, Is.EqualTo(original.Meshes[0].Id));
            Assert.That(clone.Materials[0].Id, Is.EqualTo(original.Materials[0].Id));
            Assert.That(clone.Nodes, Is.Not.SameAs(original.Nodes));
            Assert.That(clone.Meshes, Is.Not.SameAs(original.Meshes));
            Assert.That(clone.Materials, Is.Not.SameAs(original.Materials));
        });
    }
}
