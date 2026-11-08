using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: result batches → a provenance document (which node ran which tasks, with which
/// wasmtime and runtime module, at which degree) as a blob. The verdict report stays free of
/// node identity; this is where it lives. Any run doubles as a fleet inventory through it.
/// </summary>
[Activity("Verify.Provenance")]
[MemoryPackable]
public sealed partial class WitActivityVerifyProvenance : WitActivityFunction
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
        if (modelBase is not WitActivityVerifyProvenance activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results);
    }

    protected override WitActivityVerifyProvenance InnerClone()
    {
        return new WitActivityVerifyProvenance
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
