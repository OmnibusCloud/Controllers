using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Single-task variant for diagnostics and tiny jobs; mirrors the batched primitive
/// the way Render.Frame mirrors Render.FrameBatch.
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

    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
