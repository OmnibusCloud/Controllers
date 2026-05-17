using MemoryPack;
using OutWit.Common.Abstract;
using System.Linq;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Bootstrap typed-scene payload for host-side BuildBlend execution.
/// Current implementation carries a prepared .blend file inline so the typed-scene flow can be exercised end-to-end.
/// </summary>
[MemoryPackable]
public partial class RenderSceneData : ModelBase
{
    #region Properties

    /// <summary>
    /// Logical file name for the generated .blend blob.
    /// </summary>
    public string FileName { get; set; } = "scene.blend";

    /// <summary>
    /// Inline .blend payload.
    /// </summary>
    public byte[] BlendFileBytes { get; set; } = [];

    /// <summary>
    /// Addon-side metadata about dependency artifacts associated with this scene payload.
    /// </summary>
    public List<RenderSceneAttachmentRefData> AttachedFiles { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderSceneData other)
            return false;

        return string.Equals(FileName, other.FileName, StringComparison.Ordinal)
               && BlendFileBytes.AsSpan().SequenceEqual(other.BlendFileBytes)
               && AttachedFiles.Count == other.AttachedFiles.Count
               && AttachedFiles.Zip(other.AttachedFiles, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new RenderSceneData
        {
            FileName = FileName,
            BlendFileBytes = BlendFileBytes.ToArray(),
            AttachedFiles = [.. AttachedFiles.Select(me => (RenderSceneAttachmentRefData)me.Clone())]
        };
    }

    #endregion
}
