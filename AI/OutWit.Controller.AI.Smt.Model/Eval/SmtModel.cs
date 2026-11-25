using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a solver printed after <c>sat</c>, read: the answer of <c>(get-model)</c>, the answer of
/// <c>(get-value (...))</c>, or both.
/// <para>
/// The answer of <c>get-model</c> is the model: a list of <c>define-fun</c>, with the elements of
/// the sorts the problem leaves to the model as <c>declare-fun</c> without arguments ahead of
/// them; older solvers wrap the list in <c>(model ...)</c>.
/// </para>
/// <para>
/// The answer of <c>get-value</c> is a list of pairs of a term and its value. It is a claim
/// about the model, not a second model: the validator holds every pair to the model and never
/// takes a value in place of a definition (<see cref="SmtModelValidator"/>). One kind of pair
/// adds to the model: the value of a constant the problem declares and the answer of
/// <c>get-model</c> does not define is that constant's value (a solver that is asked for a value
/// its model leaves open completes the model there). So an answer of <c>get-value</c> alone is a
/// model by itself, as far as its pairs name constants, and its other pairs are held to it.
/// </para>
/// <para>
/// A text that is neither is unreadable (<see cref="IsReadable"/>), never an exception: it comes
/// from a machine nobody vouches for. A model is immutable and can be checked from several
/// threads at once.
/// </para>
/// </summary>
public sealed class SmtModel
{
    #region Constructors

    internal SmtModel(bool definitionsReadable, bool valuesReadable, SmtModelReader content)
    {
        DefinitionsReadable = definitionsReadable;
        ValuesReadable = valuesReadable;
        Functions = content.Functions;
        FunctionsInOrder = content.FunctionsInOrder;
        Reported = content.Reported;
        Completions = content.Completions;
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
    /// <param name="limits">
    /// How much of each text the reader takes before the text is unreadable:
    /// <see cref="SmtReaderLimits.Unbounded"/> for a text whose size something else has bounded
    /// already (a row of a results document), limits of the caller's own for any other.
    /// </param>
    /// <returns>The model; unreadable when a text is not what a solver prints, or is past a limit.</returns>
    /// <exception cref="ArgumentNullException">The limits are null.</exception>
    public static SmtModel Read(string? model, string? values, SmtReaderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return SmtModelReader.Read(model, values, limits);
    }

    /// <summary>
    /// Finds the value the model gives a constant: its <c>define-fun</c> without parameters, or,
    /// when the answer of <c>get-model</c> leaves the constant out, the first value the answer of
    /// <c>get-value</c> reports for it.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The value, as the solver printed it.</param>
    /// <returns>True when the model gives the constant a value.</returns>
    internal bool TryGetConstant(string name, out SmtExpression value)
    {
        if (Functions.TryGetValue(name, out var function))
        {
            value = function.Body;
            return function.Parameters.Count == 0;
        }

        return Completions.TryGetValue(name, out value!);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Whether both texts were what a solver prints. A model whose answer of <c>get-model</c> is
    /// unreadable defines nothing; one whose answer of <c>get-value</c> is unreadable keeps its
    /// definitions and reports no value, and a validation of it is never better than partial.
    /// </summary>
    public bool IsReadable => DefinitionsReadable && ValuesReadable;

    /// <summary>How many <c>define-fun</c> the answer of <c>get-model</c> holds.</summary>
    public int Definitions => Functions.Count;

    /// <summary>How many pairs of a term and its value the answer of <c>get-value</c> holds.</summary>
    public int Values => Reported.Count;

    /// <summary>How many elements the model lists for the sorts the problem leaves to it.</summary>
    public int UniverseElements => Elements.Count;

    /// <summary>Whether the answer of <c>get-model</c> was what a solver prints.</summary>
    internal bool DefinitionsReadable { get; }

    /// <summary>Whether the answer of <c>get-value</c> was what a solver prints: one list of pairs.</summary>
    internal bool ValuesReadable { get; }

    /// <summary>The <c>define-fun</c> of the answer of <c>get-model</c>, by name.</summary>
    internal IReadOnlyDictionary<string, SmtModelFunction> Functions { get; }

    /// <summary>The same definitions in the order the solver printed them.</summary>
    internal IReadOnlyList<SmtModelFunction> FunctionsInOrder { get; }

    /// <summary>
    /// The answer of <c>get-value</c>: every pair in the order the solver printed them, the term
    /// as the solver echoed it and its value as printed.
    /// </summary>
    internal IReadOnlyList<(SmtExpression Term, SmtExpression Value)> Reported { get; }

    /// <summary>
    /// For each pair of <see cref="Reported"/> whose term is one symbol, the first value reported
    /// for that symbol: what completes the model where the symbol is a constant the problem
    /// declares and the answer of <c>get-model</c> does not define.
    /// </summary>
    internal IReadOnlyDictionary<string, SmtExpression> Completions { get; }

    /// <summary>The elements of the model's universes, each as its <c>(declare-fun e () S)</c>, by name.</summary>
    internal IReadOnlyDictionary<string, SmtExpression> Elements { get; }

    /// <summary>The same elements in the order the solver printed them.</summary>
    internal IReadOnlyList<SmtExpression> ElementsInOrder { get; }

    #endregion
}
