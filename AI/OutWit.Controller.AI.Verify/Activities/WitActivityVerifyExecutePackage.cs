using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// The primitive: a node executes a package of task batches. The activity is given a
/// reference; the node downloads the package blob, runs its batches in the sandbox and
/// uploads the results as a blob. Pure function of its input — retries and reassignment
/// re-run it safely because a task's output is deterministic. A node starts every package
/// of its share together so that the blobs arrive ahead of the work; the adapter lets one
/// package execute at a time, since a batch saturates the node's cores.
/// </summary>
[Activity("Verify.ExecutePackage")]
[CanRunInParallelOnClient(true)]
[MemoryPackable]
public sealed partial class WitActivityVerifyExecutePackage : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Package}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyExecutePackage activity)
            return false;

        return base.Is(activity, tolerance)
               && Package.Check(activity.Package);
    }

    protected override WitActivityVerifyExecutePackage InnerClone()
    {
        return new WitActivityVerifyExecutePackage
        {
            Package = Package?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Package' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Package { get; init; }

    #endregion
}
