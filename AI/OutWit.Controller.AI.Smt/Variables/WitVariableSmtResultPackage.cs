using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// A package of results as it comes back from a node: a reference to a blob of rows, never the rows (what Smt.SolvePackage returns).
/// </summary>
[Variable("SmtResultPackage")]
[MemoryPackable]
public sealed partial class WitVariableSmtResultPackage : WitVariable<SmtResultPackageData?>, IWitVariableFactory<WitVariableSmtResultPackage>
{
    #region Constructors

    /// <summary>Creates the variable under a name, without a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    public WitVariableSmtResultPackage(string name)
        : base(name)
    {
    }

    /// <summary>Creates the variable under a name with a value.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <param name="value">The value.</param>
    [MemoryPackConstructor]
    public WitVariableSmtResultPackage(string name, SmtResultPackageData? value)
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
        if (modelBase is not WitVariableSmtResultPackage variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableSmtResultPackage Clone()
    {
        return new WitVariableSmtResultPackage(Name, (SmtResultPackageData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableSmtResultPackage Create(string name)
    {
        return new WitVariableSmtResultPackage(name);
    }

    #endregion
}
