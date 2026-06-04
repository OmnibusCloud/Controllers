using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Splits a render job into CHUNKS for persistent-batch distributed execution: like Render.Split but
/// it groups consecutive frames into <see cref="Model.RenderTaskBatchData"/> chunks (chunk size from
/// the per-engine default or RenderOptions.BatchSize), so each Grid.ForEach item is a chunk rendered
/// in one Blender process by Render.FrameBatch. Host-side; no Blender.
/// </summary>
[Activity("Render.SplitBatched")]
[MemoryPackable]
public sealed partial class WitActivityRenderSplitBatched : WitActivityFunction
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
        return $"Render.SplitBatched({Scene}, {StartFrame}, {EndFrame}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderSplitBatched other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderSplitBatched InnerClone()
    {
        return new WitActivityRenderSplitBatched
        {
            Scene = Scene,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            Options = Options
        };
    }

    #endregion
}
