using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Splits a render job into individual tasks for distributed execution.
/// Host-side activity: runs on server, generates RenderTaskCollection.
/// No GPU, no Blender required — pure task generation logic.
/// </summary>
[Activity("Render.Split")]
[MemoryPackable]
public sealed partial class WitActivityRenderSplit : WitActivityFunction
{
    #region Properties

    /// <summary>
    /// Blob reference to the .blend scene file.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    /// <summary>
    /// Start frame number.
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? StartFrame { get; init; }

    /// <summary>
    /// End frame number (inclusive).
    /// </summary>
    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? EndFrame { get; init; }

    /// <summary>
    /// Render options applied to each task.
    /// </summary>
    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.Split({Scene}, {StartFrame}, {EndFrame}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderSplit other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderSplit InnerClone()
    {
        return new WitActivityRenderSplit
        {
            Scene = Scene,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            Options = Options
        };
    }

    #endregion
}
