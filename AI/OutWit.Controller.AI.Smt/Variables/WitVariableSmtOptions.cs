using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// The options of a run (the second argument of SmtRun and SmtPreflight): its query, its budget of a problem, its sampling rate.
/// </summary>
[Variable("SmtOptions")]
[MemoryPackable]
public sealed partial class WitVariableSmtOptions : WitVariable<SmtOptionsData?>, IWitVariableFactory<WitVariableSmtOptions>
{
    #region Constructors

    /// <summary>Creates the variable under a name, without a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    public WitVariableSmtOptions(string name)
        : base(name)
    {
    }

    /// <summary>Creates the variable under a name with a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <param name="value">The value.</param>
    [MemoryPackConstructor]
    public WitVariableSmtOptions(string name, SmtOptionsData? value)
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
        if (modelBase is not WitVariableSmtOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableSmtOptions Clone()
    {
        return new WitVariableSmtOptions(Name, (SmtOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableSmtOptions Create(string name)
    {
        return new WitVariableSmtOptions(name);
    }

    #endregion
}
