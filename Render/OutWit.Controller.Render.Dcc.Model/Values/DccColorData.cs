using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral RGBA color contract for the first DCC scene slice.
/// </summary>
[MemoryPackable]
public partial class DccColorData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccColorData other
               && R.Is(other.R, tolerance)
               && G.Is(other.G, tolerance)
               && B.Is(other.B, tolerance)
               && A.Is(other.A, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccColorData
        {
            R = R,
            G = G,
            B = B,
            A = A
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Red component.
    /// </summary>
    public double R { get; set; }

    /// <summary>
    /// Green component.
    /// </summary>
    public double G { get; set; }

    /// <summary>
    /// Blue component.
    /// </summary>
    public double B { get; set; }

    /// <summary>
    /// Alpha component.
    /// </summary>
    public double A { get; set; } = 1d;

    #endregion
}
