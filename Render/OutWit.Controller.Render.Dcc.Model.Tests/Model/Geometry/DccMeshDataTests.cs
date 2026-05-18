using OutWit.Common.MemoryPack;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Model.Tests.Utils;

namespace OutWit.Controller.Render.Dcc.Model.Tests.Model;

[TestFixture]
public sealed class DccMeshDataTests
{
    [Test]
    public void IsTest()
    {
        var meshA = DccModelTestData.CreateMesh();
        var meshB = DccModelTestData.CreateMesh();
        var meshC = DccModelTestData.CreateMesh();
        meshC.MaterialIndices[0] = 1;

        Assert.That(meshA.Is(meshB), Is.True);
        Assert.That(meshA.Is(meshC), Is.False);
        Assert.That(meshA.Is(null!), Is.False);
    }

    [Test]
    public void CloneTest()
    {
        var original = DccModelTestData.CreateMesh();
        var clone = (DccMeshData)original.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Positions.Count, Is.EqualTo(original.Positions.Count));
            Assert.That(clone.Normals.Count, Is.EqualTo(original.Normals.Count));
            Assert.That(clone.Uv0.Count, Is.EqualTo(original.Uv0.Count));
            Assert.That(clone.TriangleIndices, Is.EqualTo(original.TriangleIndices));
            Assert.That(clone.MaterialIndices, Is.EqualTo(original.MaterialIndices));
            Assert.That(clone.Positions[0].X, Is.EqualTo(original.Positions[0].X));
            Assert.That(clone.Uv0[0].Y, Is.EqualTo(original.Uv0[0].Y));
            Assert.That(clone.Positions, Is.Not.SameAs(original.Positions));
            Assert.That(clone.Normals, Is.Not.SameAs(original.Normals));
            Assert.That(clone.Uv0, Is.Not.SameAs(original.Uv0));
            Assert.That(clone.TriangleIndices, Is.Not.SameAs(original.TriangleIndices));
            Assert.That(clone.MaterialIndices, Is.Not.SameAs(original.MaterialIndices));
        });
    }

    [Test]
    public void MemoryPackCloneTest()
    {
        var original = DccModelTestData.CreateMesh();
        var clone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.Null);
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Id, Is.EqualTo(original.Id));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Positions.Count, Is.EqualTo(original.Positions.Count));
            Assert.That(clone.Normals.Count, Is.EqualTo(original.Normals.Count));
            Assert.That(clone.Uv0.Count, Is.EqualTo(original.Uv0.Count));
            Assert.That(clone.TriangleIndices, Is.EqualTo(original.TriangleIndices));
            Assert.That(clone.MaterialIndices, Is.EqualTo(original.MaterialIndices));
            Assert.That(clone.Positions[0].X, Is.EqualTo(original.Positions[0].X));
            Assert.That(clone.Uv0[0].Y, Is.EqualTo(original.Uv0[0].Y));
            Assert.That(clone.Positions, Is.Not.SameAs(original.Positions));
            Assert.That(clone.Normals, Is.Not.SameAs(original.Normals));
            Assert.That(clone.Uv0, Is.Not.SameAs(original.Uv0));
        });
    }
}
