using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side finish: validates the emit wave is a permutation of all slabs,
/// restores slab order, and concatenates the snapshot packs plus the
/// convergence record into the single timeline blob the job returns.
/// </summary>
[Activity("Parareal.Collect")]
[MemoryPackable]
public sealed partial class WitActivityPararealCollect : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}, {Wave}, {State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealCollect activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && Wave.Check(activity.Wave)
               && State.Check(activity.State);
    }

    protected override WitActivityPararealCollect InnerClone()
    {
        return new WitActivityPararealCollect
        {
            Plan = Plan?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the slab plan; supplies the expected wave size.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Pool reference to the emit-wave results; every entry must carry a
    /// snapshot pack — iteration waves are rejected here.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    /// <summary>
    /// Pool reference to the final iteration state — the source of the
    /// convergence record written into the timeline.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
