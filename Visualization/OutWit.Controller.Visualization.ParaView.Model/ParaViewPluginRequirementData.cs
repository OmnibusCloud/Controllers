using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// A non-built-in ParaView plugin the state requires. Version 1 admits exactly one entry:
/// the OmnibusCloud .frd reader at a version the bundled reader satisfies.
/// </summary>
[JobDocumentContract("paraview.pluginRequirement@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ParaViewPluginRequirementData : ModelBase
{
    #region Properties

    /// <summary>
    /// Plugin name as ParaView registers it (for example OmnibusCloudFrdReader).
    /// </summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Minimum plugin version the state was produced with, as major.minor[.patch].
    /// </summary>
    [MemoryPackOrder(1)]
    public string Version { get; set; } = string.Empty;

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewPluginRequirementData other)
            return false;

        return Name.Is(other.Name)
               && Version.Is(other.Version);
    }

    public override ModelBase Clone()
    {
        return new ParaViewPluginRequirementData
        {
            Name = Name,
            Version = Version
        };
    }

    #endregion
}
