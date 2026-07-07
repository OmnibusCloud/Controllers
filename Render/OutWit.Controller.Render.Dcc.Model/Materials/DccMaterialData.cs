using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice material contract.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
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
               && TextureSlots.Zip(other.TextureSlots, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Transmission.Is(other.Transmission, tolerance)
               && Ior.Is(other.Ior, tolerance)
               && EmissionColor.Is(other.EmissionColor, tolerance)
               && EmissionStrength.Is(other.EmissionStrength, tolerance)
               && DisplacementScale.Is(other.DisplacementScale, tolerance)
               && BackfaceCull.Is(other.BackfaceCull)
               && EmissionCameraOnly == other.EmissionCameraOnly
               && BaseColorFromVertexColors == other.BaseColorFromVertexColors
               && EmissionFromVertexColors == other.EmissionFromVertexColors;
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
            TextureSlots = [.. TextureSlots.Select(me => (DccTextureSlotData)me.Clone())],
            Transmission = Transmission,
            Ior = Ior,
            EmissionColor = (DccColorData)EmissionColor.Clone(),
            EmissionStrength = EmissionStrength,
            DisplacementScale = DisplacementScale,
            BackfaceCull = BackfaceCull,
            EmissionCameraOnly = EmissionCameraOnly,
            BaseColorFromVertexColors = BaseColorFromVertexColors,
            EmissionFromVertexColors = EmissionFromVertexColors
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical material id.
    /// </summary>
    [MemoryPackOrder(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable material name.
    /// </summary>
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Neutral material kind.
    /// </summary>
    [MemoryPackOrder(2)]
    public DccMaterialKind Kind { get; set; } = DccMaterialKind.PrincipledSurface;

    /// <summary>
    /// Constant base color.
    /// </summary>
    [MemoryPackOrder(3)]
    public DccColorData BaseColor { get; set; } = new() { R = 1d, G = 1d, B = 1d, A = 1d };

    /// <summary>
    /// Optional base-color keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(4)]
    public List<DccColorKeyframeData> BaseColorKeyframes { get; set; } = [];

    /// <summary>
    /// Alpha handling mode for transparent materials.
    /// </summary>
    [MemoryPackOrder(5)]
    public DccMaterialAlphaMode AlphaMode { get; set; } = DccMaterialAlphaMode.Blend;

    /// <summary>
    /// Clip threshold used when AlphaMode is Clip.
    /// </summary>
    [MemoryPackOrder(6)]
    public double AlphaClipThreshold { get; set; } = 0.5d;

    /// <summary>
    /// Optional alpha-clip-threshold keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(7)]
    public List<DccScalarKeyframeData> AlphaClipThresholdKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar opacity.
    /// </summary>
    [MemoryPackOrder(8)]
    public double Opacity { get; set; } = 1d;

    /// <summary>
    /// Optional opacity keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(9)]
    public List<DccScalarKeyframeData> OpacityKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar metalness.
    /// </summary>
    [MemoryPackOrder(10)]
    public double Metallic { get; set; }

    /// <summary>
    /// Optional metallic keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(11)]
    public List<DccScalarKeyframeData> MetallicKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar roughness.
    /// </summary>
    [MemoryPackOrder(12)]
    public double Roughness { get; set; } = 0.5d;

    /// <summary>
    /// Optional roughness keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(13)]
    public List<DccScalarKeyframeData> RoughnessKeyframes { get; set; } = [];

    /// <summary>
    /// Scalar normal-map strength.
    /// </summary>
    [MemoryPackOrder(14)]
    public double NormalStrength { get; set; } = 1d;

    /// <summary>
    /// Optional normal-strength keyframes for the first material-property animation slice.
    /// </summary>
    [MemoryPackOrder(15)]
    public List<DccScalarKeyframeData> NormalStrengthKeyframes { get; set; } = [];

    /// <summary>
    /// Supported texture-slot bindings.
    /// </summary>
    [MemoryPackOrder(16)]
    public List<DccTextureSlotData> TextureSlots { get; set; } = [];

    /// <summary>
    /// Scalar light transmission (0 = opaque, 1 = fully refractive). Drives the Principled BSDF
    /// transmission weight so refractive materials such as glass render correctly.
    /// </summary>
    [MemoryPackOrder(17)]
    public double Transmission { get; set; }

    /// <summary>
    /// Index of refraction for transmissive materials (e.g. ~1.45 for glass). Only meaningful when
    /// <see cref="Transmission"/> is greater than zero.
    /// </summary>
    [MemoryPackOrder(18)]
    public double Ior { get; set; } = 1.45d;

    /// <summary>
    /// Emission color for self-illuminating materials.
    /// </summary>
    [MemoryPackOrder(19)]
    public DccColorData EmissionColor { get; set; } = new() { R = 0d, G = 0d, B = 0d, A = 1d };

    /// <summary>
    /// Emission strength (0 = no emission). Scales <see cref="EmissionColor"/> on the BSDF.
    /// </summary>
    [MemoryPackOrder(20)]
    public double EmissionStrength { get; set; }

    /// <summary>
    /// Displacement height scale, applied to the Displacement texture slot (the
    /// <see cref="DccTextureSlotKind.Displacement"/> map drives real geometry displacement). 0
    /// leaves the displacement node's default scale.
    /// </summary>
    [MemoryPackOrder(21)]
    public double DisplacementScale { get; set; } = 1d;

    /// <summary>
    /// Hide backfaces from the camera (single-sided rendering). 3ds Max Scanline culls backfaces
    /// unless a material is flagged 2-Sided, so interiors are often authored with inward-facing
    /// walls that the camera legitimately looks through from outside. Default false keeps the
    /// double-sided behaviour older payloads expect.
    /// </summary>
    [MemoryPackOrder(22)]
    public bool BackfaceCull { get; set; }


    /// <summary>
    /// Emission is a display effect only: the surface glows to camera/reflection/refraction rays
    /// but does not light the scene. Matches renderers without global illumination (3ds Max
    /// Scanline self-illumination), where a glowing surface never brightens its surroundings.
    /// </summary>
    [MemoryPackOrder(23)]
    public bool EmissionCameraOnly { get; set; }

    /// <summary>
    /// Base color is driven by the mesh's per-corner color attribute (a source-DCC vertex-color
    /// map wired into the diffuse channel) instead of <see cref="BaseColor"/>.
    /// </summary>
    [MemoryPackOrder(24)]
    public bool BaseColorFromVertexColors { get; set; }

    /// <summary>
    /// Emission color is driven by the mesh's per-corner color attribute (a source-DCC
    /// vertex-color map wired into the self-illumination channel).
    /// </summary>
    [MemoryPackOrder(25)]
    public bool EmissionFromVertexColors { get; set; }
    #endregion
}
