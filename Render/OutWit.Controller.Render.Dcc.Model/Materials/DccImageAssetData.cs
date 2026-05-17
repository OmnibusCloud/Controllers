using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Logical image-asset contract for the first DCC scene slice.
/// </summary>
[MemoryPackable]
public partial class DccImageAssetData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccImageAssetData other
               && Id.Is(other.Id)
               && Name.Is(other.Name)
               && SourcePath.Is(other.SourcePath)
               && RelativePath.Is(other.RelativePath)
               && AssetKind.Is(other.AssetKind);
    }

    public override ModelBase Clone()
    {
        return new DccImageAssetData
        {
            Id = Id,
            Name = Name,
            SourcePath = SourcePath,
            RelativePath = RelativePath,
            AssetKind = AssetKind
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Logical image id.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable image name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Original source path seen by the exporter.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Logical relative path inside the exported scene package.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Logical asset kind.
    /// </summary>
    public string AssetKind { get; set; } = string.Empty;

    #endregion
}
