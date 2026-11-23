using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// What a problem set and a run's options come to before anything is sent (the result of Smt.Preflight).
/// </summary>
[Variable("SmtPreflight")]
[MemoryPackable]
public sealed partial class WitVariableSmtPreflight : WitVariable<SmtPreflightData?>, IWitVariableFactory<WitVariableSmtPreflight>
{
    #region Constructors

    /// <summary>Creates the variable under a name, without a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    public WitVariableSmtPreflight(string name)
        : base(name)
    {
    }

    /// <summary>Creates the variable under a name with a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <param name="value">The value.</param>
    [MemoryPackConstructor]
    public WitVariableSmtPreflight(string name, SmtPreflightData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSmtPreflight variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableSmtPreflight Clone()
    {
        return new WitVariableSmtPreflight(Name, (SmtPreflightData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableSmtPreflight Create(string name)
    {
        return new WitVariableSmtPreflight(name);
    }

    #endregion
}
