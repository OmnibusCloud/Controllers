using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model.Scene;

[TestFixture]
public sealed class DccNodeDataMutationTests
{
    [Test]
    public void CloneShouldCreateDeepCopyForLocalTransform()
    {
        var original = DccModelTestData.CreateNode();
        var clone = (DccNodeData)original.Clone();

        clone.LocalTransform.Translation.X = 42d;

        Assert.Multiple(() =>
        {
            Assert.That(original.LocalTransform.Translation.X, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForTransformKeyframes()
    {
        var original = DccModelTestData.CreateNode();
        var clone = (DccNodeData)original.Clone();

        clone.TransformKeyframes[0].Transform.Translation.X = 42d;

        Assert.Multiple(() =>
        {
            Assert.That(original.TransformKeyframes[0].Transform.Translation.X, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void CloneShouldCreateDeepCopyForVisibilityKeyframes()
    {
        var original = DccModelTestData.CreateNode();
        var clone = (DccNodeData)original.Clone();

        clone.VisibilityKeyframes[0].Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(original.VisibilityKeyframes[0].Visible, Is.True);
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForLocalTransform()
    {
        var original = DccModelTestData.CreateNode();
        var clone = original.MemoryPackClone();

        clone.LocalTransform.Translation.X = 42d;

        Assert.Multiple(() =>
        {
            Assert.That(original.LocalTransform.Translation.X, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForTransformKeyframes()
    {
        var original = DccModelTestData.CreateNode();
        var clone = original.MemoryPackClone();

        clone.TransformKeyframes[0].Transform.Translation.X = 42d;

        Assert.Multiple(() =>
        {
            Assert.That(original.TransformKeyframes[0].Transform.Translation.X, Is.EqualTo(1d));
            Assert.That(original.Is(clone), Is.False);
        });
    }

    [Test]
    public void MemoryPackCloneShouldCreateDeepCopyForVisibilityKeyframes()
    {
        var original = DccModelTestData.CreateNode();
        var clone = original.MemoryPackClone();

        clone.VisibilityKeyframes[0].Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(original.VisibilityKeyframes[0].Visible, Is.True);
            Assert.That(original.Is(clone), Is.False);
        });
    }
}
