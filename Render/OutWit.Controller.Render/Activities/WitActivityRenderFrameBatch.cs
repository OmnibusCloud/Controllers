using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Renders a CHUNK of frames/tiles (a <see cref="Model.RenderTaskBatchData"/>) in a SINGLE Blender
/// process: the scene is loaded once and every task in the chunk is rendered in-process. This is the
/// persistent-batch counterpart of <see cref="WitActivityRenderFrame"/>; it amortises Blender startup
/// + scene load across the chunk. Node-side activity.
/// </summary>
/// <remarks>
/// Resource requirements: Blender needs minimum 4 GB RAM and 10 GB temp storage. GPU is NOT required.
/// Blender captures all CPU/GPU resources — parallel execution on one node is disabled.
/// </remarks>
[Activity("Render.FrameBatch")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderFrameBatch : WitActivityFunction, IRenderFrameActivity
{
    #region Properties

    /// <summary>Self-contained chunk description (scene blob, shared options, frame/tile tasks).</summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.FrameBatch({Task})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderFrameBatch other)
            return false;

        return base.Is(modelBase, tolerance)
               && Task.Check(other.Task);
    }

    protected override WitActivityRenderFrameBatch InnerClone()
    {
        return new WitActivityRenderFrameBatch
        {
            Task = Task?.Clone() as IWitParameter
        };
    }

    #endregion
}
