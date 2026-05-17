using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Render.Model;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Inline neutral DCC scene payload for the first server-side build slice.
/// </summary>
[MemoryPackable]
public partial class DccSceneData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccSceneData other
               && SceneName.Is(other.SceneName)
               && SourceApplication.Is(other.SourceApplication, tolerance)
               && Units.Is(other.Units, tolerance)
               && AxisSystem.Is(other.AxisSystem, tolerance)
               && RenderSettings.Is(other.RenderSettings, tolerance)
               && Nodes.Count == other.Nodes.Count
               && Nodes.Zip(other.Nodes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Meshes.Count == other.Meshes.Count
               && Meshes.Zip(other.Meshes, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Cameras.Count == other.Cameras.Count
               && Cameras.Zip(other.Cameras, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Lights.Count == other.Lights.Count
               && Lights.Zip(other.Lights, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Materials.Count == other.Materials.Count
               && Materials.Zip(other.Materials, (left, right) => left.Is(right, tolerance)).All(me => me)
               && ImageAssets.Count == other.ImageAssets.Count
               && ImageAssets.Zip(other.ImageAssets, (left, right) => left.Is(right, tolerance)).All(me => me)
               && AttachedFiles.Count == other.AttachedFiles.Count
               && AttachedFiles.Zip(other.AttachedFiles, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new DccSceneData
        {
            SceneName = SceneName,
            SourceApplication = (DccApplicationData)SourceApplication.Clone(),
            Units = (DccUnitSettingsData)Units.Clone(),
            AxisSystem = (DccAxisSystemData)AxisSystem.Clone(),
            RenderSettings = (DccRenderSettingsData)RenderSettings.Clone(),
            Nodes = [.. Nodes.Select(me => (DccNodeData)me.Clone())],
            Meshes = [.. Meshes.Select(me => (DccMeshData)me.Clone())],
            Cameras = [.. Cameras.Select(me => (DccCameraData)me.Clone())],
            Lights = [.. Lights.Select(me => (DccLightData)me.Clone())],
            Materials = [.. Materials.Select(me => (DccMaterialData)me.Clone())],
            ImageAssets = [.. ImageAssets.Select(me => (DccImageAssetData)me.Clone())],
            AttachedFiles = [.. AttachedFiles.Select(me => (RenderSceneAttachmentRefData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Human-readable scene name.
    /// </summary>
    public string SceneName { get; set; } = string.Empty;

    /// <summary>
    /// Source-application metadata.
    /// </summary>
    public DccApplicationData SourceApplication { get; set; } = new();

    /// <summary>
    /// Source unit metadata.
    /// </summary>
    public DccUnitSettingsData Units { get; set; } = new();

    /// <summary>
    /// Source axis-system metadata.
    /// </summary>
    public DccAxisSystemData AxisSystem { get; set; } = new();

    /// <summary>
    /// Neutral render settings.
    /// </summary>
    public DccRenderSettingsData RenderSettings { get; set; } = new();

    /// <summary>
    /// Scene nodes.
    /// </summary>
    public List<DccNodeData> Nodes { get; set; } = [];

    /// <summary>
    /// Scene meshes.
    /// </summary>
    public List<DccMeshData> Meshes { get; set; } = [];

    /// <summary>
    /// Scene cameras.
    /// </summary>
    public List<DccCameraData> Cameras { get; set; } = [];

    /// <summary>
    /// Scene lights.
    /// </summary>
    public List<DccLightData> Lights { get; set; } = [];

    /// <summary>
    /// Scene materials.
    /// </summary>
    public List<DccMaterialData> Materials { get; set; } = [];

    /// <summary>
    /// Logical image assets.
    /// </summary>
    public List<DccImageAssetData> ImageAssets { get; set; } = [];

    /// <summary>
    /// Blob-backed attachment materialization metadata.
    /// </summary>
    public List<RenderSceneAttachmentRefData> AttachedFiles { get; set; } = [];

    #endregion
}
