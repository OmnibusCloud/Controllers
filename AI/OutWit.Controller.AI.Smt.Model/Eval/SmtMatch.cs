using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// <c>match</c> of SMT-LIB 2.6 over a datatype value: the first case whose pattern fits. A
/// pattern is a constructor without fields, a constructor applied to variables, or a variable,
/// which fits everything (as does the underscore solvers take for a variable nobody reads).
/// </summary>
internal sealed class SmtMatch
{
    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly SmtEvaluator m_evaluator;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the match of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    /// <param name="evaluator">The walker of terms.</param>
    public SmtMatch(SmtRun run, SmtEvaluator evaluator)
    {
        m_run = run;
        m_meter = run.Meter;
        m_evaluator = evaluator;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Finds the case of a match that fits the matched value, and binds the variables of its pattern.
    /// </summary>
    /// <param name="items">The items of the match: the word, the matched term, the cases.</param>
    /// <param name="scope">The local names the match stands among; made when the pattern binds and there is none.</param>
    /// <param name="depth">The level of nesting the match is at.</param>
    /// <returns>The body of the case, to go on with.</returns>
    /// <exception cref="SmtPartialException">The match is not well-formed, or no case fits.</exception>
    public SmtExpression Choose(IReadOnlyList<SmtExpression> items, ref SmtScope? scope, int depth)
    {
        if (items.Count != 3 || !items[2].IsList || items[2].Items.Count == 0)
            throw m_meter.Doubt();

        if (m_evaluator.Eval(items[1], scope, depth + 1) is not SmtDatatypeValue value)
            throw m_meter.Doubt();

        foreach (var option in items[2].Items)
        {
            m_meter.Charge(1);
            if (!option.IsList || option.Items.Count != 2)
                throw m_meter.Doubt();

            if (Fits(option.Items[0], value, ref scope))
                return option.Items[1];
        }

        // The standard asks the cases to cover every constructor.
        throw m_meter.Doubt();
    }

    private bool Fits(SmtExpression pattern, SmtDatatypeValue value, ref SmtScope? scope)
    {
        if (pattern.Kind == SmtExpressionKind.Symbol)
        {
            if (TryConstructor(pattern, value, out var constant))
                return constant.Arity == 0 ? ReferenceEquals(constant, value.Constructor) : throw m_meter.Doubt();

            Bind(pattern, value, ref scope);
            return true;
        }

        if (!pattern.IsList || pattern.Items.Count < 2 || !TryConstructor(pattern.Items[0], value, out var constructor) || constructor.Arity != pattern.Items.Count - 1)
            throw m_meter.Doubt();

        // Every variable of the pattern is checked and told apart from the others, whichever
        // constructor the value has, and a constructor has as many fields as its declaration
        // gives it: the variables are paid for before the pattern is read, beside the one step
        // of the case.
        m_meter.Charge((long)constructor.Arity * SmtTerms.VARIABLE_STEPS);

        // Two wildcards are no repeated name: nothing is bound for either.
        var names = new List<string>(constructor.Arity);
        for (var index = 1; index < pattern.Items.Count; index++)
        {
            var variable = pattern.Items[index];
            if (IsWildcard(variable))
                continue;

            // The name is read as text when it is checked and told apart from the pattern's other names.
            if (SmtTerms.IsSymbol(variable))
                m_meter.ChargeName(variable.Text, SmtTerms.BINDER_READS);

            if (!SmtTerms.CanBind(variable))
                throw m_meter.Doubt();

            names.Add(variable.Text);
        }

        if (!SmtTerms.AreDistinct(names))
            throw m_meter.Doubt();

        if (!ReferenceEquals(constructor, value.Constructor))
            return false;

        for (var index = 0; index < constructor.Arity; index++)
            Bind(pattern.Items[index + 1], value.Fields[index], ref scope);

        return true;
    }

    /// <summary>
    /// Whether a symbol names a constructor. A constructor of another datatype than the matched
    /// value's is no pattern for it.
    /// </summary>
    private bool TryConstructor(SmtExpression name, SmtDatatypeValue value, out SmtConstructor constructor)
    {
        constructor = value.Constructor;
        if (name.Kind != SmtExpressionKind.Symbol)
            return false;

        m_meter.ChargeName(name.Text);
        if (!m_run.Problem.Symbols.TryGetValue(name.Text, out var symbol) || symbol.Kind != SmtSymbolKind.Constructor)
            return false;

        constructor = symbol.Constructor!;
        if (!ReferenceEquals(constructor.Datatype, value.Constructor.Datatype))
            throw m_meter.Doubt();

        return true;
    }

    private void Bind(SmtExpression variable, SmtValue value, ref SmtScope? scope)
    {
        if (IsWildcard(variable))
            return;

        // The name is read as text when it is checked, bound, and taken back at the end of the case.
        if (SmtTerms.IsSymbol(variable))
            m_meter.ChargeName(variable.Text, 1 + SmtScope.BIND_READS);

        if (!SmtTerms.CanBind(variable))
            throw m_meter.Doubt();

        scope ??= new SmtScope(false);
        scope.Bind(variable.Text, value);
    }

    private static bool IsWildcard(SmtExpression variable)
    {
        return SmtTerms.IsReserved(variable, SmtVocabulary.INDEXED);
    }

    #endregion
}
