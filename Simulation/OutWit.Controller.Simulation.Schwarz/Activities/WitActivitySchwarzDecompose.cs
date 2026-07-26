using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side opening step of the solve: partitions the model into
/// overlapping subdomains, uploads each as an immutable Subdomain blob and
/// returns the plan (blob handles, routing graph, shared solve parameters)
/// every later step reads. Rejects Coarse=true — the two-level correction
/// is reserved for v1.1.
/// </summary>
[Activity("Schwarz.Decompose")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzDecompose : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Model}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzDecompose activity)
            return false;

        return base.Is(activity, tolerance)
               && Model.Check(activity.Model)
               && Options.Check(activity.Options);
    }

    protected override WitActivitySchwarzDecompose InnerClone()
    {
        return new WitActivitySchwarzDecompose
        {
            Model = Model?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to the job-input Blob variable holding the uploaded model;
    /// resolved to a blob id and downloaded whole for partitioning.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Model { get; init; }

    /// <summary>
    /// Reference to the job-input SchwarzOptions variable that drives the
    /// split: part count (0 = default), overlap width, and the tuning scalars
    /// copied forward into the plan.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
