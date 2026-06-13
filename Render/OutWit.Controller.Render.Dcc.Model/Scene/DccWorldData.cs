using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral world / environment contract. Carries the scene background that
/// transmissive and reflective materials see, and the ambient light the world emits.
/// A null world on the scene means "no world" (empty background, no ambient).
/// </summary>
[MemoryPackable]
public partial class DccWorldData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccWorldData other
               && BackgroundColor.Is(other.BackgroundColor, tolerance)
               && Strength.Is(other.Strength, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccWorldData
        {
            BackgroundColor = (DccColorData)BackgroundColor.Clone(),
            Strength = Strength
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Constant world background / environment color.
    /// </summary>
    public DccColorData BackgroundColor { get; set; } = new() { R = 0d, G = 0d, B = 0d, A = 1d };

    /// <summary>
    /// World emission strength (scales the background as ambient light).
    /// </summary>
    public double Strength { get; set; } = 1d;

    #endregion
}
