using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// Collection of packages of results (references to blobs of rows): the output of a Grid.ForEach over Smt.SolvePackage.
/// </summary>
[Variable("SmtResultPackageCollection")]
[MemoryPackable]
public sealed partial class WitVariableSmtResultPackageCollection : WitCollection<SmtResultPackageData?>, IWitVariableFactory<WitVariableSmtResultPackageCollection>
{
    #region Constructors

    /// <summary>Creates the collection under a name, empty.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    public WitVariableSmtResultPackageCollection(string name)
        : base(name)
    {
    }

    /// <summary>Creates the collection under a name with its items.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <param name="value">The items.</param>
    [MemoryPackConstructor]
    public WitVariableSmtResultPackageCollection(string name, IReadOnlyList<SmtResultPackageData?> value)
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
        if (modelBase is not WitVariableSmtResultPackageCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <summary>Deep copy of the variable.</summary>
    /// <returns>The copy.</returns>
    public override WitVariableSmtResultPackageCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (SmtResultPackageData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableSmtResultPackageCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>Creates a variable of this type under a name.</summary>
    /// <param name="name">The variable name in the script's pool.</param>
    /// <returns>The new variable.</returns>
    public static WitVariableSmtResultPackageCollection Create(string name)
    {
        return new WitVariableSmtResultPackageCollection(name);
    }

    #endregion
}
