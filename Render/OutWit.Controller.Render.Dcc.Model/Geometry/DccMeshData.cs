using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice mesh contract.
/// </summary>
[MemoryPackable]
public partial class DccMeshData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccMeshData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && Positions.Count == other.Positions.Count
               && Positions.Zip(other.Positions, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Normals.Count == other.Normals.Count
               && Normals.Zip(other.Normals, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Uv0.Count == other.Uv0.Count
               && Uv0.Zip(other.Uv0, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Uv1.Count == other.Uv1.Count
               && Uv1.Zip(other.Uv1, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Colors.Count == other.Colors.Count
               && Colors.Zip(other.Colors, (left, right) => left.Is(right, tolerance)).All(me => me)
               && DeformationFrames.Count == other.DeformationFrames.Count
               && DeformationFrames.Zip(other.DeformationFrames, (left, right) => left.Is(right, tolerance)).All(me => me)
               && TriangleIndices.SequenceEqual(other.TriangleIndices)
               && MaterialIndices.SequenceEqual(other.MaterialIndices);
    }

    public override ModelBase Clone()
    {
        return new DccMeshData
        {
            Id = Id,
            Name = Name,
            Positions = [.. Positions.Select(me => (DccVector3Data)me.Clone())],
            Normals = [.. Normals.Select(me => (DccVector3Data)me.Clone())],
            Uv0 = [.. Uv0.Select(me => (DccVector2Data)me.Clone())],
            Uv1 = [.. Uv1.Select(me => (DccVector2Data)me.Clone())],
            Colors = [.. Colors.Select(me => (DccColorData)me.Clone())],
            DeformationFrames = [.. DeformationFrames.Select(me => (DccMeshDeformationFrameData)me.Clone())],
            TriangleIndices = [.. TriangleIndices],
            MaterialIndices = [.. MaterialIndices]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical mesh id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable mesh name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Vertex positions.
    /// </summary>
    public List<DccVector3Data> Positions { get; set; } = [];

    /// <summary>
    /// Vertex normals.
    /// </summary>
    public List<DccVector3Data> Normals { get; set; } = [];

    /// <summary>
    /// Primary UV set.
    /// </summary>
    public List<DccVector2Data> Uv0 { get; set; } = [];

    /// <summary>
    /// Optional secondary UV set (e.g. a lightmap/detail channel). Empty when the mesh has one set.
    /// </summary>
    public List<DccVector2Data> Uv1 { get; set; } = [];

    /// <summary>
    /// Flattened triangle index buffer.
    /// </summary>
    public List<int> TriangleIndices { get; set; } = [];

    /// <summary>
    /// Flattened material indices per primitive group.
    /// </summary>
    public List<int> MaterialIndices { get; set; } = [];

    // NOTE: keep new members AFTER the original ones — MemoryPack uses declaration order (no
    // [MemoryPackOrder]), so trailing additions stay wire-compatible with older payloads.

    /// <summary>
    /// Optional per-corner vertex colours (aligned with <see cref="Positions"/>). Empty when the
    /// mesh has no colour layer.
    /// </summary>
    public List<DccColorData> Colors { get; set; } = [];

    /// <summary>
    /// Optional baked deformation frames (per-frame vertex positions, each aligned with
    /// <see cref="Positions"/>). Empty for static meshes. Carries skin/morph/cloth/sim deformation
    /// as a vertex cache, applied in Blender as keyframed shape keys.
    /// </summary>
    public List<DccMeshDeformationFrameData> DeformationFrames { get; set; } = [];

    #endregion
}
