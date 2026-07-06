using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice camera contract.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
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
               && IsPerspective.Is(other.IsPerspective)
               && EnableDepthOfField.Is(other.EnableDepthOfField)
               && FocusDistance.Is(other.FocusDistance, tolerance)
               && FStop.Is(other.FStop, tolerance);
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
            IsPerspective = IsPerspective,
            EnableDepthOfField = EnableDepthOfField,
            FocusDistance = FocusDistance,
            FStop = FStop
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical camera id.
    /// </summary>
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable camera name.
    /// </summary>
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Vertical field of view in degrees.
    /// </summary>
    [MemoryPackOrder(2)]
    public double VerticalFovDegrees { get; set; }

    /// <summary>
    /// Optional FOV keyframes for the first camera-property animation slice.
    /// </summary>
    [MemoryPackOrder(3)]
    public List<DccScalarKeyframeData> VerticalFovKeyframes { get; set; } = [];

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    [MemoryPackOrder(4)]
    public double NearClip { get; set; } = 0.1d;

    /// <summary>
    /// Optional near-clip keyframes for the first camera-property animation slice.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<DccScalarKeyframeData> NearClipKeyframes { get; set; } = [];

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    [MemoryPackOrder(6)]
    public double FarClip { get; set; } = 1000d;

    /// <summary>
    /// Optional far-clip keyframes for the first camera-property animation slice.
    /// </summary>
    [MemoryPackOrder(7)]
    public List<DccScalarKeyframeData> FarClipKeyframes { get; set; } = [];

    /// <summary>
    /// True when the camera is perspective.
    /// </summary>
    [MemoryPackOrder(8)]
    public bool IsPerspective { get; set; } = true;

    /// <summary>
    /// Enables depth of field. When false the camera renders fully in focus (default).
    /// </summary>
    [MemoryPackOrder(9)]
    public bool EnableDepthOfField { get; set; }

    /// <summary>
    /// Focus distance (scene units) for depth of field. Only meaningful when
    /// <see cref="EnableDepthOfField"/> is true.
    /// </summary>
    [MemoryPackOrder(10)]
    public double FocusDistance { get; set; }

    /// <summary>
    /// Aperture f-stop for depth of field (smaller = shallower focus). Only meaningful when
    /// <see cref="EnableDepthOfField"/> is true.
    /// </summary>
    [MemoryPackOrder(11)]
    public double FStop { get; set; } = 2.8d;

    #endregion
}
