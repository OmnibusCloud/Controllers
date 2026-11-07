using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Collects exactly one render result into a single image blob (single-frame jobs). Host-side.
/// </summary>
[Activity("ParaView.CollectStill")]
[MemoryPackable]
public sealed partial class WitActivityParaViewCollectStill : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.CollectStill({Results}, {Options})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewCollectStill other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewCollectStill InnerClone()
    {
        return new WitActivityParaViewCollectStill
        {
            Results = Results,
            Options = Options
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Collection holding exactly one render result.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Results { get; init; }

    /// <summary>
    /// Output options.
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion
}
