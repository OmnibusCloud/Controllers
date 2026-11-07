using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Collects distributed render results into the ordered frame set: verifies completeness and uniqueness of
/// task identities and returns the image blobs in task order. Host-side.
/// </summary>
[Activity("ParaView.Collect")]
[MemoryPackable]
public sealed partial class WitActivityParaViewCollect : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.Collect({Results}, {Options})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewCollect other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewCollect InnerClone()
    {
        return new WitActivityParaViewCollect
        {
            Results = Results,
            Options = Options
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Collection of render results from Grid.ForEach over ParaView.RenderFrame.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Results { get; init; }

    /// <summary>
    /// Output options (recorded for format and ordering).
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion
}
