using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral 2D vector contract for the first DCC scene slice.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccVector2Data : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccVector2Data other
               && X.Is(other.X, tolerance)
               && Y.Is(other.Y, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccVector2Data
        {
            X = X,
            Y = Y
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// X component.
    /// </summary>
    [MemoryPackOrder(0)]
    public double X { get; set; }

    /// <summary>
    /// Y component.
    /// </summary>
    [MemoryPackOrder(1)]
    public double Y { get; set; }

    #endregion
}
