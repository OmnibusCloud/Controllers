using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Collects distributed render results into a final output.
/// Host-side activity: runs on server, assembles results.
/// For frame-based: returns sorted BlobCollection.
/// For tile-based (future): stitches tiles + denoise.
/// No GPU, no Blender required for frame collection.
/// </summary>
[Activity("Render.Collect")]
[MemoryPackable]
public sealed partial class WitActivityRenderCollect : WitActivityFunction
{
    #region Properties

    /// <summary>
    /// Collection of render results to assemble.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Results { get; init; }

    /// <summary>
    /// Render options (used for denoise flag, format info).
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.Collect({Results}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderCollect other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderCollect InnerClone()
    {
        return new WitActivityRenderCollect
        {
            Results = Results,
            Options = Options
        };
    }

    #endregion
}
