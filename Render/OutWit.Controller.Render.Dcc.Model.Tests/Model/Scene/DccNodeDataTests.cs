using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model;

[TestFixture]
public sealed class DccNodeDataTests
{
    [Test]
    public void IsTest()
    {
        var nodeA = DccModelTestData.CreateNode();
        var nodeB = DccModelTestData.CreateNode();
        var nodeC = DccModelTestData.CreateNode();
        nodeC.MaterialBindingId = "material:other";

        Assert.That(nodeA.Is(nodeB), Is.True);
        Assert.That(nodeA.Is(nodeC), Is.False);
        Assert.That(nodeA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateNode();
        var clone = (DccNodeData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.MeshId, Is.EqualTo(original.MeshId));
            Assert.That(clone.MaterialBindingId, Is.EqualTo(original.MaterialBindingId));
            Assert.That(clone.Visible, Is.EqualTo(original.Visible));
            Assert.That(clone.Renderable, Is.EqualTo(original.Renderable));
            Assert.That(clone.LocalTransform.Translation.X, Is.EqualTo(original.LocalTransform.Translation.X));
            Assert.That(clone.TransformKeyframes.Count, Is.EqualTo(original.TransformKeyframes.Count));
            Assert.That(clone.TransformKeyframes[0].Frame, Is.EqualTo(original.TransformKeyframes[0].Frame));
            Assert.That(clone.VisibilityKeyframes.Count, Is.EqualTo(original.VisibilityKeyframes.Count));
            Assert.That(clone.VisibilityKeyframes[0].Frame, Is.EqualTo(original.VisibilityKeyframes[0].Frame));
            Assert.That(clone.LocalTransform, Is.Not.SameAs(original.LocalTransform));
            Assert.That(clone.TransformKeyframes, Is.Not.SameAs(original.TransformKeyframes));
            Assert.That(clone.VisibilityKeyframes, Is.Not.SameAs(original.VisibilityKeyframes));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateNode();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Kind, Is.EqualTo(original.Kind));
            Assert.That(clone.MeshId, Is.EqualTo(original.MeshId));
            Assert.That(clone.MaterialBindingId, Is.EqualTo(original.MaterialBindingId));
            Assert.That(clone.Visible, Is.EqualTo(original.Visible));
            Assert.That(clone.Renderable, Is.EqualTo(original.Renderable));
            Assert.That(clone.LocalTransform.Translation.X, Is.EqualTo(original.LocalTransform.Translation.X));
            Assert.That(clone.TransformKeyframes.Count, Is.EqualTo(original.TransformKeyframes.Count));
            Assert.That(clone.TransformKeyframes[0].Frame, Is.EqualTo(original.TransformKeyframes[0].Frame));
            Assert.That(clone.VisibilityKeyframes.Count, Is.EqualTo(original.VisibilityKeyframes.Count));
            Assert.That(clone.VisibilityKeyframes[0].Frame, Is.EqualTo(original.VisibilityKeyframes[0].Frame));
            Assert.That(clone.LocalTransform, Is.Not.SameAs(original.LocalTransform));
            Assert.That(clone.TransformKeyframes, Is.Not.SameAs(original.TransformKeyframes));
            Assert.That(clone.VisibilityKeyframes, Is.Not.SameAs(original.VisibilityKeyframes));
        });
    }
}
