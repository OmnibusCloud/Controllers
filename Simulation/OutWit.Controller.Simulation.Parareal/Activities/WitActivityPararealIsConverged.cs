using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Loop-exit predicate: true once at least one correction round has run and
/// the correction norm is within Eps·Scale — the .wit grammar has no
/// arithmetic, so the comparison lives in an activity.
/// </summary>
[Activity("Parareal.IsConverged")]
[MemoryPackable]
public sealed partial class WitActivityPararealIsConverged : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealIsConverged activity)
            return false;

        return base.Is(activity, tolerance)
               && State.Check(activity.State);
    }

    protected override WitActivityPararealIsConverged InnerClone()
    {
        return new WitActivityPararealIsConverged
        {
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the iteration state whose CorrectionNorm, Eps and
    /// Scale decide the predicate.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
