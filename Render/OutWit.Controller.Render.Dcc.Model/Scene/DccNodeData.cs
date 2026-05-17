using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice scene-node contract.
/// </summary>
[MemoryPackable]
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
               && Renderable.Is(other.Renderable);
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
            Renderable = Renderable
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical node id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable node name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent node id when present.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Neutral node kind.
    /// </summary>
    public DccNodeKind Kind { get; set; } = DccNodeKind.Mesh;

    /// <summary>
    /// Local node transform.
    /// </summary>
    public DccTransformData LocalTransform { get; set; } = new();

    /// <summary>
    /// Optional transform keyframes for the first animation-aware slice.
    /// </summary>
    public List<DccTransformKeyframeData> TransformKeyframes { get; set; } = [];

    /// <summary>
    /// Optional visibility/renderability keyframes for the first animation-aware slice.
    /// </summary>
    public List<DccVisibilityKeyframeData> VisibilityKeyframes { get; set; } = [];

    /// <summary>
    /// Referenced mesh id when the node is a mesh instance.
    /// </summary>
    public string? MeshId { get; set; }

    /// <summary>
    /// Referenced camera id when the node is a camera.
    /// </summary>
    public string? CameraId { get; set; }

    /// <summary>
    /// Referenced light id when the node is a light.
    /// </summary>
    public string? LightId { get; set; }

    /// <summary>
    /// Bound material id for the node when present.
    /// </summary>
    public string? MaterialBindingId { get; set; }

    /// <summary>
    /// True when the node is visible.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// True when the node is renderable.
    /// </summary>
    public bool Renderable { get; set; } = true;

    #endregion
}
