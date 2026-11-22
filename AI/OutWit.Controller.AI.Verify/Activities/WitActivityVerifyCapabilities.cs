using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: what a submitter can know before a run, as a JSON document blob — the
/// controller's version, the runtimes it carries, the defaults and ceilings a taskset is held
/// to, and the pool as totals (nodes, concurrent sandboxes, reference tasks per second; no
/// node is named). Takes no taskset, so an application can ask once and show it next to its
/// own preflight.
/// </summary>
[Activity("Verify.Capabilities")]
[MemoryPackable]
public sealed partial class WitActivityVerifyCapabilities : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Verify.Capabilities";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityVerifyCapabilities activity && base.Is(activity, tolerance);
    }

    protected override WitActivityVerifyCapabilities InnerClone()
    {
        return new WitActivityVerifyCapabilities();
    }

    #endregion
}
