using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// A chunk of render tasks that share one scene, rendered together in a SINGLE Blender process: the
/// .blend is loaded once and every frame/tile in <see cref="Tasks"/> is rendered in-process. This is
/// the persistent-batch unit produced by Render.Split's per-engine chunking and consumed by
/// Render.FrameBatch. The shared <see cref="SceneBlobId"/> and <see cref="Options"/> are hoisted once
/// (one device/engine/resolution configuration per process); each <see cref="Tasks"/> entry carries
/// only its own Frame + tile bounds + TaskIndex.
/// </summary>
[MemoryPackable]
public partial class RenderTaskBatchData : ModelBase
{
    #region Properties

    /// <summary>Blob identifier of the .blend scene file (shared by every task in the chunk).</summary>
    public Guid SceneBlobId { get; set; }

    /// <summary>Render options shared by the chunk (engine/device/resolution configured once).</summary>
    [MemoryPackAllowSerialize]
    public RenderOptionsData Options { get; set; } = new();

    /// <summary>The frames/tiles rendered together in one Blender process, in submit order.</summary>
    [MemoryPackAllowSerialize]
    public List<RenderTaskData> Tasks { get; set; } = new();

    /// <summary>
    /// Scene dependency artifacts shared by every task in the chunk (the chunk shares one scene) that
    /// the node materializes next to the blend before rendering. Populated host-side by
    /// Render.SplitBatched from the attachment sidecar written by Render.BuildBlendFromRefs; empty for
    /// self-contained scenes. Appended last for MemoryPack wire back-compatibility.
    /// </summary>
    public List<RenderSceneAttachmentRefData> Attachments { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderTaskBatchData other)
            return false;

        if (Tasks.Count != other.Tasks.Count)
            return false;

        for (int i = 0; i < Tasks.Count; i++)
        {
            if (!Tasks[i].Is(other.Tasks[i], tolerance))
                return false;
        }

        if (Attachments.Count != other.Attachments.Count)
            return false;

        for (int i = 0; i < Attachments.Count; i++)
        {
            if (!Attachments[i].Is(other.Attachments[i], tolerance))
                return false;
        }

        return SceneBlobId.Is(other.SceneBlobId)
               && Options.Is(other.Options, tolerance);
    }

    public override ModelBase Clone()
    {
        return new RenderTaskBatchData
        {
            SceneBlobId = SceneBlobId,
            Options = (RenderOptionsData)Options.Clone(),
            Tasks = Tasks.Select(task => (RenderTaskData)task.Clone()).ToList(),
            Attachments = Attachments.Select(me => (RenderSceneAttachmentRefData)me.Clone()).ToList()
        };
    }

    #endregion
}
