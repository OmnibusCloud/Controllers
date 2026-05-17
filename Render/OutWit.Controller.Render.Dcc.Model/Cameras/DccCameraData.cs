using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice camera contract.
/// </summary>
[MemoryPackable]
public partial class DccCameraData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccCameraData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && VerticalFovDegrees.Is(other.VerticalFovDegrees, tolerance)
               && VerticalFovKeyframes.Count == other.VerticalFovKeyframes.Count
               && VerticalFovKeyframes.Zip(other.VerticalFovKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && NearClip.Is(other.NearClip, tolerance)
               && NearClipKeyframes.Count == other.NearClipKeyframes.Count
               && NearClipKeyframes.Zip(other.NearClipKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && FarClip.Is(other.FarClip, tolerance)
               && FarClipKeyframes.Count == other.FarClipKeyframes.Count
               && FarClipKeyframes.Zip(other.FarClipKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && IsPerspective.Is(other.IsPerspective);
    }

    public override ModelBase Clone()
    {
        return new DccCameraData
        {
            Id = Id,
            Name = Name,
            VerticalFovDegrees = VerticalFovDegrees,
            VerticalFovKeyframes = [.. VerticalFovKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            NearClip = NearClip,
            NearClipKeyframes = [.. NearClipKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            FarClip = FarClip,
            FarClipKeyframes = [.. FarClipKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            IsPerspective = IsPerspective
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical camera id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable camera name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Vertical field of view in degrees.
    /// </summary>
    public double VerticalFovDegrees { get; set; }

    /// <summary>
    /// Optional FOV keyframes for the first camera-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> VerticalFovKeyframes { get; set; } = [];

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public double NearClip { get; set; } = 0.1d;

    /// <summary>
    /// Optional near-clip keyframes for the first camera-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> NearClipKeyframes { get; set; } = [];

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public double FarClip { get; set; } = 1000d;

    /// <summary>
    /// Optional far-clip keyframes for the first camera-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> FarClipKeyframes { get; set; } = [];

    /// <summary>
    /// True when the camera is perspective.
    /// </summary>
    public bool IsPerspective { get; set; } = true;

    #endregion
}
