using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Single-task variant for diagnostics: one task, given inline, runs as a batch of one. A
/// taskset never travels this way; it goes through Verify.Split and Verify.ExecutePackage.
/// </summary>
[Activity("Verify.Execute")]
[MemoryPackable]
public sealed partial class WitActivityVerifyExecute : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Task}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyExecute activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivityVerifyExecute InnerClone()
    {
        return new WitActivityVerifyExecute
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Task' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
