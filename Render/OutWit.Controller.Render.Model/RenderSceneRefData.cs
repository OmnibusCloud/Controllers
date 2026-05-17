using MemoryPack;
using OutWit.Common.Abstract;
using System.Linq;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Bootstrap blob-backed typed-scene reference for host-side BuildBlendFromRefs execution.
/// Current implementation points directly at a prepared .blend blob in storage.
/// </summary>
[MemoryPackable]
public partial class RenderSceneRefData : ModelBase
{
    #region Properties

    /// <summary>
    /// Blob identifier of a prepared .blend payload.
    /// </summary>
    public Guid BlendBlobId { get; set; }

    /// <summary>
    /// Addon-side metadata about dependency artifacts associated with the referenced scene payload.
    /// </summary>
    public List<RenderSceneAttachmentRefData> AttachedFiles { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is RenderSceneRefData other
               && BlendBlobId == other.BlendBlobId
               && AttachedFiles.Count == other.AttachedFiles.Count
               && AttachedFiles.Zip(other.AttachedFiles, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new RenderSceneRefData
        {
            BlendBlobId = BlendBlobId,
            AttachedFiles = [.. AttachedFiles.Select(me => (RenderSceneAttachmentRefData)me.Clone())]
        };
    }

    #endregion
}
