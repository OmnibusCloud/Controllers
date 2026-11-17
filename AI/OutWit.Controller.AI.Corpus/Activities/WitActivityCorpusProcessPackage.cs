using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// The primitive: a node takes one task package (a blob of shard tasks), fetches each shard from
/// its public origin, runs it through the pipeline and returns one result package (a blob of
/// results). A pure function of each task — the product's hash is the same on any node — so
/// retries and reassignment re-run it safely. Several packages may run on one client at once;
/// the node holds and processes only a few shards at a time whatever its share is.
/// </summary>
[Activity("Corpus.ProcessPackage")]
[CanRunInParallelOnClient(true)]
[MemoryPackable]
public sealed partial class WitActivityCorpusProcessPackage : WitActivityFunction
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
        if (modelBase is not WitActivityCorpusProcessPackage activity)
            return false;

        return base.Is(activity, tolerance)
               && Package.Check(activity.Package);
    }

    protected override WitActivityCorpusProcessPackage InnerClone()
    {
        return new WitActivityCorpusProcessPackage
        {
            Package = Package?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Package' argument of the activity: a task package reference.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Package { get; init; }

    #endregion
}
