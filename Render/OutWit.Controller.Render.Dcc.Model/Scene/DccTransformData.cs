using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Local transform contract for a neutral DCC scene node.
/// </summary>
[MemoryPackable]
public partial class DccTransformData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccTransformData other
               && Translation.Is(other.Translation, tolerance)
               && Rotation.Is(other.Rotation, tolerance)
               && Scale.Is(other.Scale, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccTransformData
        {
            Translation = (DccVector3Data)Translation.Clone(),
            Rotation = (DccQuaternionData)Rotation.Clone(),
            Scale = (DccVector3Data)Scale.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Local translation.
    /// </summary>
    public DccVector3Data Translation { get; set; } = new();

    /// <summary>
    /// Local rotation quaternion.
    /// </summary>
    public DccQuaternionData Rotation { get; set; } = new();

    /// <summary>
    /// Local scale.
    /// </summary>
    public DccVector3Data Scale { get; set; } = new() { X = 1d, Y = 1d, Z = 1d };

    #endregion
}
