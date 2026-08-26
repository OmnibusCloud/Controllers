using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Splits a validated package into deterministic render tasks (as ParaView.Split does) and groups
/// consecutive outputs into chunks one pvpython process renders together (FrameBatch, docs 03,
/// section 27, item 2), each chunk carrying the union of its outputs' attachment subsets. Host-side,
/// pure task generation.
/// </summary>
[Activity("ParaView.SplitBatched")]
[MemoryPackable]
public sealed partial class WitActivityParaViewSplitBatched : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.SplitBatched({Scene}, {Report}, {Options})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewSplitBatched other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewSplitBatched InnerClone()
    {
        return new WitActivityParaViewSplitBatched
        {
            Scene = Scene,
            Report = Report,
            Options = Options
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Blob-backed package reference.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    /// <summary>
    /// The validation report of ParaView.Validate for the same package and options.
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Report { get; init; }

    /// <summary>
    /// Output options.
    /// </summary>
    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion
}
