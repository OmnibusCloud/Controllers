using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: the primary batches + options → the replica batches of the sampled share of
/// tasks (<c>SamplingRate</c>), re-planned so the allocator places them on its own; empty at
/// rate 0. The second fan-out of every script runs them, and <c>Verify.Integrity</c>
/// compares the two executions.
/// </summary>
[Activity("Verify.Sample")]
[MemoryPackable]
public sealed partial class WitActivityVerifySample : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Batches}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifySample activity)
            return false;

        return base.Is(activity, tolerance)
               && Batches.Check(activity.Batches)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifySample InnerClone()
    {
        return new WitActivityVerifySample
        {
            Batches = Batches?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Batches' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Batches { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
