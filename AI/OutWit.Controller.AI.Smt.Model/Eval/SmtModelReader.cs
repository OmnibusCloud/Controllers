using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Reads the two answers a solver prints after <c>sat</c> into a model. It is strict: each
/// answer is one list, every item of the answer of <c>get-model</c> is a <c>define-fun</c>, an
/// element of a universe, or the cardinality constraint the solver prints after the elements,
/// and no name is defined twice; every item of the answer of <c>get-value</c> is a pair of a
/// term and a value. Anything else is not what a solver prints, and that answer is unreadable.
/// <para>
/// The two answers are read apart. An answer of <c>get-model</c> that is unreadable leaves
/// nothing to check. An answer of <c>get-value</c> that is unreadable does not hide the model:
/// the definitions stay, no value is reported, and the model says so
/// (<see cref="SmtModel.ValuesReadable"/>).
/// </para>
/// </summary>
internal sealed class SmtModelReader
{
    #region Constants

    private const string MODEL = "model";
    private const string DEFINE_FUN = "define-fun";
    private const string DECLARE_FUN = "declare-fun";

    #endregion

    #region Fields

    private readonly Dictionary<string, SmtModelFunction> m_functions = new(StringComparer.Ordinal);
    private readonly List<SmtModelFunction> m_functionsInOrder = [];
    private readonly List<(SmtExpression Term, SmtExpression Value)> m_reported = [];
    private readonly Dictionary<string, SmtExpression> m_completions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtExpression> m_elements = new(StringComparer.Ordinal);
    private readonly List<SmtExpression> m_elementsInOrder = [];

    #endregion

    #region Functions

    /// <summary>
    /// Reads the answers.
    /// </summary>
    /// <param name="model">The answer of <c>(get-model)</c>; null or blank when there is none.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>; null or blank when there is none.</param>
    /// <param name="limits">How much of each text the reader takes.</param>
    /// <returns>The model; unreadable when a text is not what a solver prints.</returns>
    public static SmtModel Read(string? model, string? values, SmtReaderLimits limits)
    {
        var reader = new SmtModelReader();
        if (!reader.TryReadDefinitions(model, limits))
            return new SmtModel(false, false, new SmtModelReader());

        return new SmtModel(true, reader.TryReadValues(values, limits), reader);
    }

    private bool TryReadDefinitions(string? text, SmtReaderLimits limits)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!TryReadList(text, limits, out var items))
            return false;

        // Older solvers print (model (define-fun ...) ...).
        var first = items.Count > 0 && items[0].IsSymbol(MODEL) ? 1 : 0;
        for (var index = first; index < items.Count; index++)
        {
            if (!TryReadDefinition(items[index]))
                return false;
        }

        return true;
    }

    private bool TryReadDefinition(SmtExpression item)
    {
        if (!item.IsList || item.Items.Count == 0 || !SmtTerms.IsSymbol(item.Items[0]) || item.Items[0].IsQuoted)
            return false;

        var items = item.Items;
        switch (items[0].Text)
        {
            case DEFINE_FUN:
                return items.Count == 5 && IsFree(items[1]) && SmtTerms.TrySortedVariables(items[2], out var names, out var sorts) && Add(new SmtModelFunction(item, names, sorts));
            case DECLARE_FUN:
                // An element of a universe: a constant the model declares and does not define.
                return items.Count == 4 && IsFree(items[1]) && items[2].IsList && items[2].Items.Count == 0 && Add(item);
            case SmtVocabulary.FORALL:
                // The cardinality constraint of a universe: it says the elements are all there are.
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads the answer of <c>get-value</c>: every pair is kept, whatever its term is, since
    /// every pair is a claim the validator checks. A list with an item that is no pair reports
    /// nothing at all.
    /// </summary>
    private bool TryReadValues(string? text, SmtReaderLimits limits)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!TryReadList(text, limits, out var pairs))
            return false;

        foreach (var pair in pairs)
        {
            if (!pair.IsList || pair.Items.Count != 2)
            {
                m_reported.Clear();
                m_completions.Clear();
                return false;
            }

            m_reported.Add((pair.Items[0], pair.Items[1]));

            // The first value reported for a symbol is the one that may complete the model; a
            // later pair of the same symbol is a claim like any other.
            if (SmtTerms.IsSymbol(pair.Items[0]))
                m_completions.TryAdd(pair.Items[0].Text, pair.Items[1]);
        }

        return true;
    }

    private static bool TryReadList(string text, SmtReaderLimits limits, out IReadOnlyList<SmtExpression> items)
    {
        items = [];
        IReadOnlyList<SmtExpression> read;
        try
        {
            read = SmtReader.Read(text, limits);
        }
        catch (SmtSyntaxException)
        {
            return false;
        }

        if (read.Count != 1 || !read[0].IsList)
            return false;

        items = read[0].Items;
        return true;
    }

    /// <summary>
    /// Whether a name can be defined by the model: a symbol the model has not used yet, and
    /// none of the names that mean something without a declaration.
    /// </summary>
    private bool IsFree(SmtExpression name)
    {
        return SmtTerms.IsSymbol(name) && !SmtVocabulary.IsOperator(name.Text) && !m_functions.ContainsKey(name.Text) && !m_elements.ContainsKey(name.Text);
    }

    private bool Add(SmtModelFunction function)
    {
        m_functions.Add(function.Name, function);
        m_functionsInOrder.Add(function);
        return true;
    }

    private bool Add(SmtExpression element)
    {
        m_elements.Add(element.Items[1].Text, element);
        m_elementsInOrder.Add(element);
        return true;
    }

    #endregion

    #region Properties

    /// <summary>The definitions, by name.</summary>
    public IReadOnlyDictionary<string, SmtModelFunction> Functions => m_functions;

    /// <summary>The definitions, in the order printed.</summary>
    public IReadOnlyList<SmtModelFunction> FunctionsInOrder => m_functionsInOrder;

    /// <summary>The answer of <c>get-value</c>: every pair, in the order printed.</summary>
    public IReadOnlyList<(SmtExpression Term, SmtExpression Value)> Reported => m_reported;

    /// <summary>The first value the answer of <c>get-value</c> reports for each term that is one symbol, by the symbol's name.</summary>
    public IReadOnlyDictionary<string, SmtExpression> Completions => m_completions;

    /// <summary>The elements of the universes, each as its declaration, by name.</summary>
    public IReadOnlyDictionary<string, SmtExpression> Elements => m_elements;

    /// <summary>The elements of the universes, in the order printed.</summary>
    public IReadOnlyList<SmtExpression> ElementsInOrder => m_elementsInOrder;

    #endregion
}
