using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The model a solver printed after <c>sat</c>, read: the answer of <c>(get-model)</c>, the
/// answer of <c>(get-value (...))</c>, or both. Where both give a value to the same constant,
/// the answer of <c>get-value</c> is the one taken.
/// <para>
/// The answer of <c>get-model</c> is a list of <c>define-fun</c>, with the elements of the sorts
/// the problem leaves to the model as <c>declare-fun</c> without arguments ahead of them; older
/// solvers wrap the list in <c>(model ...)</c>. The answer of <c>get-value</c> is a list of
/// pairs of a term and its value.
/// </para>
/// <para>
/// A text that is neither is an unreadable model (<see cref="IsReadable"/>), never an
/// exception: it comes from a machine nobody vouches for. A model is immutable and can be
/// checked from several threads at once.
/// </para>
/// </summary>
public sealed class SmtModel
{
    #region Constructors

    internal SmtModel(bool isReadable, SmtModelReader content)
    {
        IsReadable = isReadable;
        Functions = content.Functions;
        FunctionsInOrder = content.FunctionsInOrder;
        Constants = content.Constants;
        Elements = content.Elements;
        ElementsInOrder = content.ElementsInOrder;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads what a solver printed after <c>sat</c>.
    /// </summary>
    /// <param name="model">The answer of <c>(get-model)</c>; null or blank when there is none.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>; null or blank when there is none.</param>
    /// <param name="limits">How much of each text the reader takes before the model is unreadable.</param>
    /// <returns>The model; unreadable when a text is not what a solver prints, or is past a limit.</returns>
    /// <exception cref="ArgumentNullException">The limits are null.</exception>
    public static SmtModel Read(string? model, string? values, SmtReaderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return SmtModelReader.Read(model, values, limits);
    }

    /// <summary>
    /// Finds the value the model gives a constant: the answer of <c>get-value</c> first, then a
    /// <c>define-fun</c> without parameters.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The value, as the solver printed it.</param>
    /// <returns>True when the model gives the constant a value.</returns>
    internal bool TryGetConstant(string name, out SmtExpression value)
    {
        if (Constants.TryGetValue(name, out value!))
            return true;

        if (Functions.TryGetValue(name, out var function) && function.Parameters.Count == 0)
        {
            value = function.Body;
            return true;
        }

        return false;
    }

    #endregion

    #region Properties

    /// <summary>Whether the texts were what a solver prints. An unreadable model defines nothing.</summary>
    public bool IsReadable { get; }

    /// <summary>How many <c>define-fun</c> the answer of <c>get-model</c> holds.</summary>
    public int Definitions => Functions.Count;

    /// <summary>How many constants the answer of <c>get-value</c> gives a value.</summary>
    public int Values => Constants.Count;

    /// <summary>How many elements the model lists for the sorts the problem leaves to it.</summary>
    public int UniverseElements => Elements.Count;

    /// <summary>The <c>define-fun</c> of the answer of <c>get-model</c>, by name.</summary>
    internal IReadOnlyDictionary<string, SmtModelFunction> Functions { get; }

    /// <summary>The same definitions in the order the solver printed them.</summary>
    internal IReadOnlyList<SmtModelFunction> FunctionsInOrder { get; }

    /// <summary>The answer of <c>get-value</c>: a constant's name and its value as printed.</summary>
    internal IReadOnlyDictionary<string, SmtExpression> Constants { get; }

    /// <summary>The elements of the model's universes, each as its <c>(declare-fun e () S)</c>, by name.</summary>
    internal IReadOnlyDictionary<string, SmtExpression> Elements { get; }

    /// <summary>The same elements in the order the solver printed them.</summary>
    internal IReadOnlyList<SmtExpression> ElementsInOrder { get; }

    #endregion
}
