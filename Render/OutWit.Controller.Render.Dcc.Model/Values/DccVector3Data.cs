using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral 3D vector contract for the first DCC scene slice.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccVector3Data : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccVector3Data other
               && X.Is(other.X, tolerance)
               && Y.Is(other.Y, tolerance)
               && Z.Is(other.Z, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccVector3Data
        {
            X = X,
            Y = Y,
            Z = Z
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

    /// <summary>
    /// Z component.
    /// </summary>
    [MemoryPackOrder(2)]
    public double Z { get; set; }

    #endregion
}
