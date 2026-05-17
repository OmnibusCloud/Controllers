using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Validates whether the current packaged runtime can execute a video render request.
/// </summary>
[Activity("Render.PreflightVideo")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderPreflightVideo : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Video { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.PreflightVideo({Options}, {Video})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderPreflightVideo && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderPreflightVideo InnerClone()
    {
        return new WitActivityRenderPreflightVideo
        {
            Options = Options,
            Video = Video
        };
    }

    #endregion
}
