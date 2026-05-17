using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Dcc.Models.Build;

internal sealed class DccSceneBuildInput
{
    #region Properties

    public DccSceneData Scene { get; init; } = null!;

    public double UnitsToMetersScale { get; init; }

    public IReadOnlyDictionary<string, DccNodeData> NodesById { get; init; } = new Dictionary<string, DccNodeData>();

    public IReadOnlyDictionary<string, DccMeshData> MeshesById { get; init; } = new Dictionary<string, DccMeshData>();

    public IReadOnlyDictionary<string, DccMaterialData> MaterialsById { get; init; } = new Dictionary<string, DccMaterialData>();

    public IReadOnlyDictionary<string, DccImageAssetData> ImageAssetsById { get; init; } = new Dictionary<string, DccImageAssetData>();

    public IReadOnlyDictionary<string, RenderSceneAttachmentRefData> ImageAttachmentsByImageId { get; init; } = new Dictionary<string, RenderSceneAttachmentRefData>();

    #endregion
}
