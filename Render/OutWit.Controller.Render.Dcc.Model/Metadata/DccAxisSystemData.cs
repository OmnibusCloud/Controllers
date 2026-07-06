using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Axis-system metadata for a neutral DCC scene payload.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccAxisSystemData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccAxisSystemData other
               && Handedness.Is(other.Handedness)
               && UpAxis.Is(other.UpAxis)
               && ForwardAxis.Is(other.ForwardAxis);
    }

    public override ModelBase Clone()
    {
        return new DccAxisSystemData
        {
            Handedness = Handedness,
            UpAxis = UpAxis,
            ForwardAxis = ForwardAxis
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Source handedness.
    /// </summary>
    [MemoryPackOrder(0)]
    public string Handedness { get; set; } = string.Empty;

    /// <summary>
    /// Source up axis.
    /// </summary>
    [MemoryPackOrder(1)]
    public string UpAxis { get; set; } = string.Empty;

    /// <summary>
    /// Source forward axis.
    /// </summary>
    [MemoryPackOrder(2)]
    public string ForwardAxis { get; set; } = string.Empty;

    #endregion
}
