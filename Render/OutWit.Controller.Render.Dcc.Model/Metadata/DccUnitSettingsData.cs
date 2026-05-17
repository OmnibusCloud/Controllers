using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Unit metadata for a neutral DCC scene payload.
/// </summary>
[MemoryPackable]
public partial class DccUnitSettingsData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccUnitSettingsData other
               && LinearUnit.Is(other.LinearUnit)
               && UnitsPerMeter.Is(other.UnitsPerMeter, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccUnitSettingsData
        {
            LinearUnit = LinearUnit,
            UnitsPerMeter = UnitsPerMeter
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical linear-unit name.
    /// </summary>
    public string LinearUnit { get; set; } = string.Empty;

    /// <summary>
    /// Number of source units per one meter.
    /// </summary>
    public double UnitsPerMeter { get; set; } = 1d;

    #endregion
}
