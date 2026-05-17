using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral quaternion rotation contract for the first DCC scene slice.
/// </summary>
[MemoryPackable]
public partial class DccQuaternionData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccQuaternionData other
               && X.Is(other.X, tolerance)
               && Y.Is(other.Y, tolerance)
               && Z.Is(other.Z, tolerance)
               && W.Is(other.W, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccQuaternionData
        {
            X = X,
            Y = Y,
            Z = Z,
            W = W
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// X component.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y component.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Z component.
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// W component.
    /// </summary>
    public double W { get; set; } = 1d;

    #endregion
}
