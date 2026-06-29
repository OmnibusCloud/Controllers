using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Metadata about one addon-side dependency artifact associated with a scene reference.
/// </summary>
[MemoryPackable]
public partial class RenderSceneAttachmentRefData : ModelBase
{
    #region Properties

    /// <summary>
    /// Logical dependency kind, for example ImageAsset or Font.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Cloud blob identifier of the uploaded dependency artifact when it is transferred separately from the blend file.
    /// </summary>
    public Guid BlobId { get; set; }

    /// <summary>
    /// Original addon-side source path observed in the Blender scene.
    /// </summary>
    public string OriginalPath { get; set; } = string.Empty;

    /// <summary>
    /// Logical relative path or slot name inside the addon-side scene package.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Packaging strategy attempted by the addon for this dependency.
    /// </summary>
    public string PackagingStrategy { get; set; } = string.Empty;

    /// <summary>
    /// Frame this artifact belongs to, for per-frame baked simulation caches (one cache file per frame).
    /// null = applies to all frames (textures, linked libraries, and other non-frame-specific deps).
    /// Lets Render.Split* send each render node only the cache files for the frames it renders.
    /// Appended last for MemoryPack wire back-compatibility.
    /// </summary>
    public int? Frame { get; set; }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderSceneAttachmentRefData other)
            return false;

        return Kind.Is(other.Kind)
               && BlobId.Is(other.BlobId)
               && OriginalPath.Is(other.OriginalPath)
               && RelativePath.Is(other.RelativePath)
               && PackagingStrategy.Is(other.PackagingStrategy)
               && Frame == other.Frame;
    }

    public override ModelBase Clone()
    {
        return new RenderSceneAttachmentRefData
        {
            Kind = Kind,
            BlobId = BlobId,
            OriginalPath = OriginalPath,
            RelativePath = RelativePath,
            PackagingStrategy = PackagingStrategy,
            Frame = Frame
        };
    }

    #endregion
}
