using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice scene-node contract.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccNodeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccNodeData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && ParentId.Is(other.ParentId)
               && Kind.Is(other.Kind)
               && LocalTransform.Is(other.LocalTransform, tolerance)
               && TransformKeyframes.Count == other.TransformKeyframes.Count
               && TransformKeyframes.Zip(other.TransformKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && VisibilityKeyframes.Count == other.VisibilityKeyframes.Count
               && VisibilityKeyframes.Zip(other.VisibilityKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && MeshId.Is(other.MeshId)
               && CameraId.Is(other.CameraId)
               && LightId.Is(other.LightId)
               && MaterialBindingId.Is(other.MaterialBindingId)
               && Visible.Is(other.Visible)
               && Renderable.Is(other.Renderable)
               && IsBackdrop == other.IsBackdrop;
    }

    public override ModelBase Clone()
    {
        return new DccNodeData
        {
            Id = Id,
            Name = Name,
            ParentId = ParentId,
            Kind = Kind,
            LocalTransform = (DccTransformData)LocalTransform.Clone(),
            TransformKeyframes = [.. TransformKeyframes.Select(me => (DccTransformKeyframeData)me.Clone())],
            VisibilityKeyframes = [.. VisibilityKeyframes.Select(me => (DccVisibilityKeyframeData)me.Clone())],
            MeshId = MeshId,
            CameraId = CameraId,
            LightId = LightId,
            MaterialBindingId = MaterialBindingId,
            Visible = Visible,
            Renderable = Renderable,
            IsBackdrop = IsBackdrop
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical node id.
    /// </summary>
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable node name.
    /// </summary>
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent node id when present.
    /// </summary>
    [MemoryPackOrder(2)]
    public string? ParentId { get; set; }

    /// <summary>
    /// Neutral node kind.
    /// </summary>
    [MemoryPackOrder(3)]
    public DccNodeKind Kind { get; set; } = DccNodeKind.Mesh;

    /// <summary>
    /// Local node transform.
    /// </summary>
    [MemoryPackOrder(4)]
    public DccTransformData LocalTransform { get; set; } = new();

    /// <summary>
    /// Optional transform keyframes for the first animation-aware slice.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<DccTransformKeyframeData> TransformKeyframes { get; set; } = [];

    /// <summary>
    /// Optional visibility/renderability keyframes for the first animation-aware slice.
    /// </summary>
    [MemoryPackOrder(6)]
    public List<DccVisibilityKeyframeData> VisibilityKeyframes { get; set; } = [];

    /// <summary>
    /// Referenced mesh id when the node is a mesh instance.
    /// </summary>
    [MemoryPackOrder(7)]
    public string? MeshId { get; set; }

    /// <summary>
    /// Referenced camera id when the node is a camera.
    /// </summary>
    [MemoryPackOrder(8)]
    public string? CameraId { get; set; }

    /// <summary>
    /// Referenced light id when the node is a light.
    /// </summary>
    [MemoryPackOrder(9)]
    public string? LightId { get; set; }

    /// <summary>
    /// Bound material id for the node when present.
    /// </summary>
    [MemoryPackOrder(10)]
    public string? MaterialBindingId { get; set; }

    /// <summary>
    /// True when the node is visible.
    /// </summary>
    [MemoryPackOrder(11)]
    public bool Visible { get; set; } = true;

    /// <summary>
    /// True when the node is renderable.
    /// </summary>
    [MemoryPackOrder(12)]
    public bool Renderable { get; set; } = true;

    /// <summary>
    /// Marks a mesh node as a scene backdrop (e.g. a sky-dome shell): it is shown to camera,
    /// reflection and refraction rays but must not light the scene, cast shadows or occlude
    /// lights — the source application treats such shells as scenery, not luminaires.
    /// </summary>
    [MemoryPackOrder(13)]
    public bool IsBackdrop { get; set; }

    #endregion
}
