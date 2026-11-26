using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Variables;

/// <summary>
/// Collection of packages of results (references to blobs of rows): the output of a Grid.ForEach over Smt.SolvePackage.
/// <para>
/// It asks Grid which package each result was returned for (<see cref="OnGridAnswer"/>): a result
/// names the ticket of the package it answers, and that is the node's word, while Grid knows which
/// task, and so which package, a value came back for. The host remembers the pair for the result
/// object (<see cref="SmtResultBindings"/>), never in the collection's items: nothing of it is
/// copied by <see cref="Clone"/> or travels with the collection.
/// </para>
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

    #region Grid

    /// <summary>
    /// What Grid (1.4.0 and later) tells a return collection of a <c>Grid.ForEach</c>, once for
    /// every value it keeps and before the value joins the results: the item of the call's
    /// collection the task was built from, and the value the node returned for it, both the very
    /// objects. For a package of problems and a result package the host remembers that the result
    /// was returned for that package; anything else is not remembered. It never throws (a throw
    /// would leave the result unbound) and may be called from several threads at once.
    /// </summary>
    /// <param name="item">The item the task was built from; null or of another type for a call over something else.</param>
    /// <param name="value">The value the node returned for the task.</param>
    public void OnGridAnswer(object? item, object? value)
    {
        SmtResultBindings.Bind(item, value);
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
