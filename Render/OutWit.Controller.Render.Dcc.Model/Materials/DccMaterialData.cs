using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice material contract.
/// </summary>
[MemoryPackable]
public partial class DccMaterialData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccMaterialData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && Kind.Is(other.Kind)
               && BaseColor.Is(other.BaseColor, tolerance)
               && BaseColorKeyframes.Count == other.BaseColorKeyframes.Count
               && BaseColorKeyframes.Zip(other.BaseColorKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && AlphaMode.Is(other.AlphaMode)
               && AlphaClipThreshold.Is(other.AlphaClipThreshold, tolerance)
               && AlphaClipThresholdKeyframes.Count == other.AlphaClipThresholdKeyframes.Count
               && AlphaClipThresholdKeyframes.Zip(other.AlphaClipThresholdKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Opacity.Is(other.Opacity, tolerance)
               && OpacityKeyframes.Count == other.OpacityKeyframes.Count
               && OpacityKeyframes.Zip(other.OpacityKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Metallic.Is(other.Metallic, tolerance)
               && MetallicKeyframes.Count == other.MetallicKeyframes.Count
               && MetallicKeyframes.Zip(other.MetallicKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Roughness.Is(other.Roughness, tolerance)
               && RoughnessKeyframes.Count == other.RoughnessKeyframes.Count
               && RoughnessKeyframes.Zip(other.RoughnessKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && NormalStrength.Is(other.NormalStrength, tolerance)
               && NormalStrengthKeyframes.Count == other.NormalStrengthKeyframes.Count
               && NormalStrengthKeyframes.Zip(other.NormalStrengthKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && TextureSlots.Count == other.TextureSlots.Count
               && TextureSlots.Zip(other.TextureSlots, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new DccMaterialData
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            BaseColor = (DccColorData)BaseColor.Clone(),
            BaseColorKeyframes = [.. BaseColorKeyframes.Select(me => (DccColorKeyframeData)me.Clone())],
            AlphaMode = AlphaMode,
            AlphaClipThreshold = AlphaClipThreshold,
            AlphaClipThresholdKeyframes = [.. AlphaClipThresholdKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            Opacity = Opacity,
            OpacityKeyframes = [.. OpacityKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            Metallic = Metallic,
            MetallicKeyframes = [.. MetallicKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            Roughness = Roughness,
            RoughnessKeyframes = [.. RoughnessKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            NormalStrength = NormalStrength,
            NormalStrengthKeyframes = [.. NormalStrengthKeyframes.Select(me => (DccScalarKeyframeData)me.Clone())],
            TextureSlots = [.. TextureSlots.Select(me => (DccTextureSlotData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical material id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable material name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Neutral material kind.
    /// </summary>
    public DccMaterialKind Kind { get; set; } = DccMaterialKind.PrincipledSurface;

    /// <summary>
    /// Constant base color.
    /// </summary>
    public DccColorData BaseColor { get; set; } = new() { R = 1d, G = 1d, B = 1d, A = 1d };

    /// <summary>
    /// Optional base-color keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccColorKeyframeData> BaseColorKeyframes { get; set; } = [];

    /// <summary>
    /// Alpha handling mode for transparent materials.
    /// </summary>
    public DccMaterialAlphaMode AlphaMode { get; set; } = DccMaterialAlphaMode.Blend;

    /// <summary>
    /// Clip threshold used when AlphaMode is Clip.
    /// </summary>
    public double AlphaClipThreshold { get; set; } = 0.5d;

    /// <summary>
    /// Optional alpha-clip-threshold keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> AlphaClipThresholdKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar opacity.
    /// </summary>
    public double Opacity { get; set; } = 1d;

    /// <summary>
    /// Optional opacity keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> OpacityKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar metalness.
    /// </summary>
    public double Metallic { get; set; }

    /// <summary>
    /// Optional metallic keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> MetallicKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar roughness.
    /// </summary>
    public double Roughness { get; set; } = 0.5d;

    /// <summary>
    /// Optional roughness keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> RoughnessKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar normal-map strength.
    /// </summary>
    public double NormalStrength { get; set; } = 1d;

    /// <summary>
    /// Optional normal-strength keyframes for the first material-property animation slice.
    /// </summary>
    public List<DccScalarKeyframeData> NormalStrengthKeyframes { get; set; } = [];

    /// <summary>
    /// Supported texture-slot bindings.
    /// </summary>
    public List<DccTextureSlotData> TextureSlots { get; set; } = [];

    #endregion
}
