using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: the result batches of all executions → the integrity document (the executions of
/// every task that ran more than once paired, the nodes of each side, agreement on verdict,
/// exit, outputs and cases; pairs that met on one node counted, not compared; the mismatch
/// rate) as a blob.
/// </summary>
[Activity("Verify.Integrity")]
[MemoryPackable]
public sealed partial class WitActivityVerifyIntegrity : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Results}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyIntegrity activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results);
    }

    protected override WitActivityVerifyIntegrity InnerClone()
    {
        return new WitActivityVerifyIntegrity
        {
            Results = Results?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Results' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    #endregion
}
