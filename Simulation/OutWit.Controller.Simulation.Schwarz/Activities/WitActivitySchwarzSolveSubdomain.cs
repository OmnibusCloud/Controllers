using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Node-side subdomain solve — a pure function of its task: retries and
/// reassignment re-run it safely because outputs are new immutable blobs.
/// Single per client: the real solver saturates cores via ThreadsPerNode.
/// </summary>
[Activity("Schwarz.SolveSubdomain")]
[CanRunInParallelOnClient(false)]
[RequiresResources(MinRamMb = 4096)]
[MemoryPackable]
public sealed partial class WitActivitySchwarzSolveSubdomain : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Task}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzSolveSubdomain activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivitySchwarzSolveSubdomain InnerClone()
    {
        return new WitActivitySchwarzSolveSubdomain
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to this invocation's SchwarzTask — the per-item binding
    /// Grid.ForEach makes from the task collection; carries the subdomain blob
    /// handle, incoming boundary blob ids, round and the emit flag whole,
    /// because the transformer takes exactly one argument.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
