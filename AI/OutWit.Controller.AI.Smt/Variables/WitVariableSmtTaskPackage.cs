using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// A package of problems as it goes to a node: a reference to a blob, never the problems (the argument of Smt.SolvePackage).
/// </summary>
[Variable("SmtTaskPackage")]
[MemoryPackable]
public sealed partial class WitVariableSmtTaskPackage : WitVariable<SmtTaskPackageData?>, IWitVariableFactory<WitVariableSmtTaskPackage>
{
    #region Constructors

    /// <summary>Creates the variable under a name, without a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    public WitVariableSmtTaskPackage(string name)
        : base(name)
    {
    }

    /// <summary>Creates the variable under a name with a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <param name="value">The value.</param>
    [MemoryPackConstructor]
    public WitVariableSmtTaskPackage(string name, SmtTaskPackageData? value)
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
        if (modelBase is not WitVariableSmtTaskPackage variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableSmtTaskPackage Clone()
    {
        return new WitVariableSmtTaskPackage(Name, (SmtTaskPackageData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableSmtTaskPackage Create(string name)
    {
        return new WitVariableSmtTaskPackage(name);
    }

    #endregion
}
