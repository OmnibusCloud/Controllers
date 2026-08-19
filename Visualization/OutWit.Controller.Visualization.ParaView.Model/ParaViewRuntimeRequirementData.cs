using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// What the package was produced with and what it needs to be rendered: the producing ParaView
/// version (exact major and minor are mandatory in the version-1 compatibility policy), the
/// producing plugin and platform (provenance), and the non-built-in plugins the state requires.
/// </summary>
[JobDocumentContract("paraview.runtimeRequirement@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ParaViewRuntimeRequirementData : ModelBase
{
    #region Properties

    /// <summary>
    /// Major version of the ParaView that produced the state.
    /// </summary>
    [MemoryPackOrder(0)]
    public int ParaViewMajor { get; set; }

    /// <summary>
    /// Minor version of the ParaView that produced the state.
    /// </summary>
    [MemoryPackOrder(1)]
    public int ParaViewMinor { get; set; }

    /// <summary>
    /// Patch version of the ParaView that produced the state (informational; patch mismatch is tolerated).
    /// </summary>
    [MemoryPackOrder(2)]
    public int ParaViewPatch { get; set; }

    /// <summary>
    /// Version of the producing OmnibusCloud ParaView plugin (provenance).
    /// </summary>
    [MemoryPackOrder(3)]
    public string ProducerPluginVersion { get; set; } = string.Empty;

    /// <summary>
    /// Runtime identifier of the producing platform, for example win-x64 (provenance).
    /// </summary>
    [MemoryPackOrder(4)]
    public string ProducerPlatform { get; set; } = string.Empty;

    /// <summary>
    /// Non-built-in plugins the state requires. Unknown names are rejected; version 1 allowlists
    /// only the bundled OmnibusCloud reader.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<ParaViewPluginRequirementData> Plugins { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewRuntimeRequirementData other)
            return false;

        return ParaViewMajor.Is(other.ParaViewMajor)
               && ParaViewMinor.Is(other.ParaViewMinor)
               && ParaViewPatch.Is(other.ParaViewPatch)
               && ProducerPluginVersion.Is(other.ProducerPluginVersion)
               && ProducerPlatform.Is(other.ProducerPlatform)
               && Plugins.Count == other.Plugins.Count
               && Plugins.Zip(other.Plugins, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new ParaViewRuntimeRequirementData
        {
            ParaViewMajor = ParaViewMajor,
            ParaViewMinor = ParaViewMinor,
            ParaViewPatch = ParaViewPatch,
            ProducerPluginVersion = ProducerPluginVersion,
            ProducerPlatform = ProducerPlatform,
            Plugins = [.. Plugins.Select(me => (ParaViewPluginRequirementData)me.Clone())]
        };
    }

    #endregion
}
