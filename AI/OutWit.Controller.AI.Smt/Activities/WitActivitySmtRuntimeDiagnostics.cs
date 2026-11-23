using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Reports what the Smt controller of the process that evaluates it carries and can do: its
/// version and profile, the solver module's pin and whether the process has it and has compiled
/// it, the engine library, the trap mode, the platform, and the degree a package would get
/// (<c>SmtRuntimeDiagnostics</c>). A plain script function runs on the host, so this describes the
/// host's module; the fleet's are exercised by <c>Smt.SolvePackage</c> inside <c>Grid.ForEach</c>.
/// Nothing in it names the machine.
/// </summary>
[Activity("Smt.RuntimeDiagnostics")]
[MemoryPackable]
public sealed partial class WitActivitySmtRuntimeDiagnostics : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>Its name.</returns>
    protected override string InnerString()
    {
        return "Smt.RuntimeDiagnostics";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivitySmtRuntimeDiagnostics activity && base.Is(activity, tolerance);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtRuntimeDiagnostics InnerClone()
    {
        return new WitActivitySmtRuntimeDiagnostics();
    }

    #endregion
}
