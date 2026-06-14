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
    public void VertexColorsRoundTripTest()
    {
        var original = DccModelTestData.CreateMesh();
        original.Colors =
        [
            new DccColorData { R = 1d, G = 0d, B = 0d, A = 1d },
            new DccColorData { R = 0d, G = 1d, B = 0d, A = 1d },
            new DccColorData { R = 0d, G = 0d, B = 1d, A = 0.5d }
        ];

        var differentColor = (DccMeshData)original.Clone();
        differentColor.Colors[2].B = 0.25d;

        var clone = (DccMeshData)original.Clone();
        var memoryPackClone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(original.Is(differentColor), Is.False);
            Assert.That(clone.Colors.Count, Is.EqualTo(3));
            Assert.That(clone.Colors, Is.Not.SameAs(original.Colors));
            Assert.That(clone.Is(original), Is.True);
            Assert.That(memoryPackClone.Colors.Count, Is.EqualTo(3));
            Assert.That(memoryPackClone.Colors[0].R, Is.EqualTo(1d));
            Assert.That(memoryPackClone.Colors[2].A, Is.EqualTo(0.5d));
            Assert.That(memoryPackClone.Is(original), Is.True);
        });
    }

    [Test]
    public void DeformationFramesRoundTripTest()
    {
        var original = DccModelTestData.CreateMesh();
        original.DeformationFrames =
        [
            new DccMeshDeformationFrameData
            {
                Frame = 1,
                Positions = [.. original.Positions.Select(me => (DccVector3Data)me.Clone())]
            },
            new DccMeshDeformationFrameData
            {
                Frame = 2,
                Positions = [.. original.Positions.Select(me => new DccVector3Data { X = me.X + 1d, Y = me.Y, Z = me.Z })]
            }
        ];

        var differentFrame = (DccMeshData)original.Clone();
        differentFrame.DeformationFrames[1].Positions[0].X += 5d;

        var clone = (DccMeshData)original.Clone();
        var memoryPackClone = original.MemoryPackClone();

        Assert.Multiple(() =>
        {
            Assert.That(original.Is(differentFrame), Is.False);
            Assert.That(clone.DeformationFrames.Count, Is.EqualTo(2));
            Assert.That(clone.DeformationFrames, Is.Not.SameAs(original.DeformationFrames));
            Assert.That(clone.Is(original), Is.True);
            Assert.That(memoryPackClone.DeformationFrames.Count, Is.EqualTo(2));
            Assert.That(memoryPackClone.DeformationFrames[1].Frame, Is.EqualTo(2));
            Assert.That(memoryPackClone.DeformationFrames[1].Positions.Count, Is.EqualTo(original.Positions.Count));
            Assert.That(memoryPackClone.Is(original), Is.True);
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
