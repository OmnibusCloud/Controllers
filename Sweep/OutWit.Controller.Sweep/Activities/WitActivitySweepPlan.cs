using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Validates the submitted study - the study itself and its family's block,
/// and the cases of an OpenFOAM case set when the script passes one - and
/// computes the immutable execution plan, chunk schedule included.
/// </summary>
[Activity("Sweep.Plan")]
[MemoryPackable]
public sealed partial class WitActivitySweepPlan : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return OpenFOAMSet == null ? $"{Options}" : $"{Options}, {OpenFOAMSet}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepPlan activity)
            return false;

        return base.Is(activity, tolerance)
               && Options.Check(activity.Options)
               && OpenFOAMSet.Check(activity.OpenFOAMSet);
    }

    protected override WitActivitySweepPlan InnerClone()
    {
        return new WitActivitySweepPlan
        {
            Options = Options?.Clone() as IWitReference,
            OpenFOAMSet = OpenFOAMSet?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the Options argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    /// <summary>Reference to the optional OpenFOAMSet argument (the cases of a case-set study); null otherwise.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? OpenFOAMSet { get; init; }

    #endregion
}
