using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral world / environment contract. Carries the scene background that
/// transmissive and reflective materials see, and the ambient light the world emits.
/// A null world on the scene means "no world" (empty background, no ambient).
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccWorldData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccWorldData other
               && BackgroundColor.Is(other.BackgroundColor, tolerance)
               && Strength.Is(other.Strength, tolerance)
               && EnvironmentImageId == other.EnvironmentImageId
               && EnvironmentRotationDegrees.Is(other.EnvironmentRotationDegrees, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccWorldData
        {
            BackgroundColor = (DccColorData)BackgroundColor.Clone(),
            Strength = Strength,
            EnvironmentImageId = EnvironmentImageId,
            EnvironmentRotationDegrees = EnvironmentRotationDegrees
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Constant world background / environment color.
    /// </summary>
    [MemoryPackOrder(0)]
    public DccColorData BackgroundColor { get; set; } = new() { R = 0d, G = 0d, B = 0d, A = 1d };

    /// <summary>
    /// World emission strength (scales the background / environment as ambient light).
    /// </summary>
    [MemoryPackOrder(1)]
    public double Strength { get; set; } = 1d;

    /// <summary>
    /// Optional id of a <see cref="DccImageAssetData"/> used as an equirectangular environment
    /// (HDRI) for the world. When set, the environment image drives the background and ambient
    /// lighting instead of <see cref="BackgroundColor"/>. Null/empty = use the constant color.
    /// </summary>
    [MemoryPackOrder(2)]
    public string? EnvironmentImageId { get; set; }

    /// <summary>
    /// Z-axis rotation (degrees) applied to the environment image, so the HDRI can be oriented.
    /// Ignored when <see cref="EnvironmentImageId"/> is not set.
    /// </summary>
    [MemoryPackOrder(3)]
    public double EnvironmentRotationDegrees { get; set; }

    #endregion
}
