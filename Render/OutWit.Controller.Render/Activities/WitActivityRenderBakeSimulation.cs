using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Bakes a scene's sequential simulation (v1: Mantaflow fluid/gas) into a per-frame OpenVDB cache, on a
/// single node. Dispatched via <c>Grid.Delegate</c>, so it lands on the fastest compatible node by the same
/// render-throughput benchmark used for frame distribution. Takes the original scene reference + the render
/// frame range + bake options; returns a new <c>RenderSceneRef</c> whose blend is the baked scene and whose
/// AttachedFiles carry the original dependencies plus the per-frame, Frame-tagged VDB cache files. Feeding
/// that scene ref back through <c>Render.BuildBlendFromRefs → Render.SplitBatched</c> reuses the proven
/// per-frame slicing path so each node renders its frame from only its own cache slice.
/// </summary>
[Activity("Render.BakeSimulation")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderBakeSimulation : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? StartFrame { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? EndFrame { get; init; }

    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.BakeSimulation({Scene}, {StartFrame}, {EndFrame}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderBakeSimulation other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderBakeSimulation InnerClone()
    {
        return new WitActivityRenderBakeSimulation
        {
            Scene = Scene,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            Options = Options
        };
    }

    #endregion
}
