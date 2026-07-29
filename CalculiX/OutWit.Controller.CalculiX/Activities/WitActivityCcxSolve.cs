using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Activities;

/// <summary>
/// Node-side whole-deck solve: downloads the variant deck, runs the bundled
/// ccx in a scratch directory, uploads the result artifacts and extracts the
/// requested responses right there. A pure function of its task — retries and
/// reassignment re-run it safely because outputs are new immutable blobs.
/// Single per client: ccx saturates cores via its own OMP threading, and two
/// concurrent solves would double peak RAM.
/// </summary>
[Activity("Ccx.Solve")]
[CanRunInParallelOnClient(false)]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 2048)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityCcxSolve : WitActivityFunction
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
        if (modelBase is not WitActivityCcxSolve activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivityCcxSolve InnerClone()
    {
        return new WitActivityCcxSolve
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to this invocation's CcxTask — the per-item binding
    /// Grid.ForEach makes from the task collection; carries the deck blob id,
    /// the explicit mesh counts and the extraction request whole, because the
    /// transformer takes exactly one argument.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
