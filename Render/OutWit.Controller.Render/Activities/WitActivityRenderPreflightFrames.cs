using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Validates whether the current packaged runtime can execute a frame-based render request.
/// </summary>
[Activity("Render.PreflightFrames")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderPreflightFrames : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? StartFrame { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? EndFrame { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.PreflightFrames({StartFrame}, {EndFrame}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderPreflightFrames && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderPreflightFrames InnerClone()
    {
        return new WitActivityRenderPreflightFrames
        {
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            Options = Options
        };
    }

    #endregion
}
