using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: the ledger of a settled run (Verify.Settle) → its outputs (one JSON line a task) as a blob of its own. The
/// document was written when the run was settled; nothing is decided here and no node's result
/// is read.
/// </summary>
[Activity("Verify.CollectOutputs")]
[MemoryPackable]
public sealed partial class WitActivityVerifyCollectOutputs : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Ledger}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyCollectOutputs activity)
            return false;

        return base.Is(activity, tolerance)
               && Ledger.Check(activity.Ledger)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifyCollectOutputs InnerClone()
    {
        return new WitActivityVerifyCollectOutputs
        {
            Ledger = Ledger?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Ledger' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Ledger { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
