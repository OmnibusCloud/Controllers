using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice light contract.
/// </summary>
[MemoryPackable]
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
               && SpotAngleKeyframes.Zip(other.SpotAngleKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me);
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
            SpotAngleKeyframes = [.. SpotAngleKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical light id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable light name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Neutral light kind.
    /// </summary>
    public DccLightKind Kind { get; set; } = DccLightKind.Point;

    /// <summary>
    /// Light color.
    /// </summary>
    public DccColorData Color { get; set; } = new() { R = 1d, G = 1d, B = 1d, A = 1d };

    /// <summary>
    /// Optional color keyframes for the first light-property animation slice.
    /// </summary>
    public List<DccColorKeyframeData> ColorKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar light intensity.
    /// </summary>
    public double Intensity { get; set; } = 1d;

    /// <summary>
    /// Optional intensity keyframes for the first light-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> IntensityKeyframes { get; set; } = [];

    /// <summary>
    /// Light range.
    /// </summary>
    public double Range { get; set; } = 10d;

    /// <summary>
    /// Optional range keyframes for the first light-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> RangeKeyframes { get; set; } = [];

    /// <summary>
    /// Spot angle in degrees.
    /// </summary>
    public double SpotAngleDegrees { get; set; } = 45d;

    /// <summary>
    /// Optional spot-angle keyframes for the first light-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> SpotAngleKeyframes { get; set; } = [];

    #endregion
}
