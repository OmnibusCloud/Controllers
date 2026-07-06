using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Source-application metadata for a neutral DCC scene payload.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccApplicationData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccApplicationData other
               && ApplicationFamily.Is(other.ApplicationFamily)
               && ApplicationVersion.Is(other.ApplicationVersion)
               && ExporterVersion.Is(other.ExporterVersion);
    }

    public override ModelBase Clone()
    {
        return new DccApplicationData
        {
            ApplicationFamily = ApplicationFamily,
            ApplicationVersion = ApplicationVersion,
            ExporterVersion = ExporterVersion
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Exporting application family, for example 3dsMax.
    /// </summary>
    [MemoryPackOrder(0)]
    public string ApplicationFamily { get; set; } = string.Empty;

    /// <summary>
    /// Exporting application version.
    /// </summary>
    [MemoryPackOrder(1)]
    public string ApplicationVersion { get; set; } = string.Empty;

    /// <summary>
    /// Exporter version that produced the scene contract.
    /// </summary>
    [MemoryPackOrder(2)]
    public string ExporterVersion { get; set; } = string.Empty;

    #endregion
}
