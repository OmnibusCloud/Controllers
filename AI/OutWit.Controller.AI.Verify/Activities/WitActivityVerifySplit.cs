using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: parse the taskset blob, apply limit ceilings, and chunk by runtime affinity
/// into the batches Grid.ForEach fans out.
/// </summary>
[Activity("Verify.Split")]
[MemoryPackable]
public sealed partial class WitActivityVerifySplit : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Taskset}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifySplit activity)
            return false;

        return base.Is(activity, tolerance)
               && Taskset.Check(activity.Taskset)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifySplit InnerClone()
    {
        return new WitActivityVerifySplit
        {
            Taskset = Taskset?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Taskset { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
