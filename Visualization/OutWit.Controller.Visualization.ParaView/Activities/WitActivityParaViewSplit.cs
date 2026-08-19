using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Splits a validated package into deterministic independent render tasks, one per resolved timestep,
/// each carrying only the attachments its timestep needs. Host-side, pure task generation.
/// </summary>
[Activity("ParaView.Split")]
[MemoryPackable]
public sealed partial class WitActivityParaViewSplit : WitActivityFunction
{
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

    #region Functions

    protected override string InnerString()
    {
        return $"ParaView.Split({Scene}, {Report}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewSplit other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    protected override WitActivityParaViewSplit InnerClone()
    {
        return new WitActivityParaViewSplit
        {
            Scene = Scene,
            Report = Report,
            Options = Options
        };
    }

    #endregion
}
