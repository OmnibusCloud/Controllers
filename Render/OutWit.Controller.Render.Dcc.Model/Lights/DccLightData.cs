using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice light contract.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccLightData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccLightData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && Kind.Is(other.Kind)
               && Color.Is(other.Color, tolerance)
               && ColorKeyframes.Count == other.ColorKeyframes.Count
               && ColorKeyframes.Zip(other.ColorKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Intensity.Is(other.Intensity, tolerance)
               && IntensityKeyframes.Count == other.IntensityKeyframes.Count
               && IntensityKeyframes.Zip(other.IntensityKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Range.Is(other.Range, tolerance)
               && RangeKeyframes.Count == other.RangeKeyframes.Count
               && RangeKeyframes.Zip(other.RangeKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && SpotAngleDegrees.Is(other.SpotAngleDegrees, tolerance)
               && SpotAngleKeyframes.Count == other.SpotAngleKeyframes.Count
               && SpotAngleKeyframes.Zip(other.SpotAngleKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && CastShadows.Is(other.CastShadows)
               && AreaWidth.Is(other.AreaWidth, tolerance)
               && AreaHeight.Is(other.AreaHeight, tolerance)
               && SpotBlend.Is(other.SpotBlend, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccLightData
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            Color = (DccColorData)Color.Clone(),
            ColorKeyframes = [.. ColorKeyframes.Select(me => (DccColorKeyframeData)me.Clone())],
            Intensity = Intensity,
            IntensityKeyframes = [.. IntensityKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            Range = Range,
            RangeKeyframes = [.. RangeKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            SpotAngleDegrees = SpotAngleDegrees,
            SpotAngleKeyframes = [.. SpotAngleKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            CastShadows = CastShadows,
            AreaWidth = AreaWidth,
            SpotBlend = SpotBlend,
            AreaHeight = AreaHeight
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical light id.
    /// </summary>
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable light name.
    /// </summary>
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Neutral light kind.
    /// </summary>
    [MemoryPackOrder(2)]
    public DccLightKind Kind { get; set; } = DccLightKind.Point;

    /// <summary>
    /// Light color.
    /// </summary>
    [MemoryPackOrder(3)]
    public DccColorData Color { get; set; } = new() { R = 1d, G = 1d, B = 1d, A = 1d };

    /// <summary>
    /// Optional color keyframes for the first light-property animation slice.
    /// </summary>
    [MemoryPackOrder(4)]
    public List<DccColorKeyframeData> ColorKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar light intensity.
    /// </summary>
    [MemoryPackOrder(5)]
    public double Intensity { get; set; } = 1d;

    /// <summary>
    /// Optional intensity keyframes for the first light-property animation slice.
    /// </summary>
    [MemoryPackOrder(6)]
    public List<DccScalarKeyframeData> IntensityKeyframes { get; set; } = [];

    /// <summary>
    /// Light range.
    /// </summary>
    [MemoryPackOrder(7)]
    public double Range { get; set; } = 10d;

    /// <summary>
    /// Optional range keyframes for the first light-property animation slice.
    /// </summary>
    [MemoryPackOrder(8)]
    public List<DccScalarKeyframeData> RangeKeyframes { get; set; } = [];

    /// <summary>
    /// Spot angle in degrees.
    /// </summary>
    [MemoryPackOrder(9)]
    public double SpotAngleDegrees { get; set; } = 45d;

    /// <summary>
    /// Optional spot-angle keyframes for the first light-property animation slice.
    /// </summary>
    [MemoryPackOrder(10)]
    public List<DccScalarKeyframeData> SpotAngleKeyframes { get; set; } = [];

    /// <summary>
    /// Whether the light casts shadows (default true).
    /// </summary>
    [MemoryPackOrder(11)]
    public bool CastShadows { get; set; } = true;

    /// <summary>
    /// Area-light width (scene units). Only meaningful when <see cref="Kind"/> is Area.
    /// </summary>
    [MemoryPackOrder(12)]
    public double AreaWidth { get; set; } = 1d;

    /// <summary>
    /// Area-light height (scene units). Only meaningful when <see cref="Kind"/> is Area.
    /// </summary>
    [MemoryPackOrder(13)]
    public double AreaHeight { get; set; } = 1d;

    /// <summary>
    /// Spot edge softness in the [0, 1] range (0 = hard edge). Carries the source hotspot/falloff
    /// cone difference; only meaningful when <see cref="Kind"/> is Spot.
    /// </summary>
    [MemoryPackOrder(14)]
    public double SpotBlend { get; set; }

    #endregion
}
