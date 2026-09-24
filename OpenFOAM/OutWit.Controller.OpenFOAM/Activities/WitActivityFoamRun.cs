using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Activities;

/// <summary>
/// Node-side whole-case run: materialises the variant's case from the base
/// files and its substitutions, runs the allow-listed recipe under the
/// bundled OpenFOAM kit in a scratch directory, reads the convergence facts
/// and the requested responses from the finished case, uploads the artifact
/// the policy asks for. A pure function of its task - retries and
/// reassignment re-run it safely because outputs are new immutable blobs.
/// Single per client: a parallel step takes every core it is given, and two
/// concurrent cases would double peak RAM.
/// </summary>
[Activity("Foam.Run")]
[CanRunInParallelOnClient(false)]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 4096)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityFoamRun : WitActivityFunction
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
        if (modelBase is not WitActivityFoamRun activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivityFoamRun InnerClone()
    {
        return new WitActivityFoamRun
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to this invocation's FoamTask - the per-item binding
    /// Grid.ForEach makes from the task collection; carries the base files,
    /// the substitutions, the recipe and the extraction request whole, because
    /// the transformer takes exactly one argument.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
