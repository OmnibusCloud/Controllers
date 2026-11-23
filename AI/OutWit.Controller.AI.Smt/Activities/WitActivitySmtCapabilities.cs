using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: what a lab can know before a run, as a JSON document blob
/// (<c>docs/smt-capabilities.schema.json</c>): the controller's version and solver profile, the
/// queries, the defaults and ceilings of a run's options and of a problem, the words of a row,
/// what a node runs a package with, and the pool as totals (nodes, sessions at once, reference
/// problems a second; no node is named). Takes no problem set, so an application can ask once and
/// show it beside its own preflight.
/// </summary>
[Activity("Smt.Capabilities")]
[MemoryPackable]
public sealed partial class WitActivitySmtCapabilities : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>Its name.</returns>
    protected override string InnerString()
    {
        return "Smt.Capabilities";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivitySmtCapabilities activity && base.Is(activity, tolerance);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtCapabilities InnerClone()
    {
        return new WitActivitySmtCapabilities();
    }

    #endregion
}
