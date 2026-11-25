using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Arrays that are written as functions of the index: the function a solver prints behind an
/// array, and <c>lambda</c>. Where the body is the chain a solver prints,
/// <c>(ite (= x c1) v1 (ite (= x c2) v2 ... otherwise))</c> with no other mention of x, the
/// function is exactly a finite map, and the array becomes one: only a finite map can be
/// compared with another array. Any other body stays a function: the array can be read at an
/// index, by evaluating the body there.
/// </summary>
internal sealed class SmtArrayFunctions
{
    #region Constants

    private const string EQUAL = "=";

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly SmtArrays m_arrays;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the function arrays of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    /// <param name="arrays">The arrays.</param>
    public SmtArrayFunctions(SmtRun run, SmtArrays arrays)
    {
        m_run = run;
        m_meter = run.Meter;
        m_arrays = arrays;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The array a function of one argument stands for.
    /// </summary>
    /// <param name="function">The function.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The array: a finite map when the body allows, over the function otherwise.</returns>
    /// <exception cref="SmtPartialException">The body is not evaluated.</exception>
    public SmtArrayValue ToArray(SmtFunction function, int depth)
    {
        var indexSort = function.ParameterSorts[0];
        if (function.Body == null)
        {
            // A symbol the model left out: the array that holds the default value everywhere.
            m_meter.Complete();
            return new SmtArrayValue(indexSort, function.ResultSort, m_run.Conformance.Default(function.ResultSort!, depth), SmtArrayValue.NO_ENTRIES);
        }

        var scope = new SmtScope(function.IsModel);
        return TryFiniteMap(function.Parameters[0], function.Body, scope, indexSort, function.ResultSort, depth)
               ?? new SmtArrayValue(indexSort, function.ResultSort, new SmtArraySource(function), SmtArrayValue.NO_ENTRIES);
    }

    /// <summary>
    /// The array a <c>lambda</c> of one variable stands for.
    /// </summary>
    /// <param name="items">The lambda's items: the word, the variable with its sort, the body.</param>
    /// <param name="scope">The local names the lambda stands among, or null.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The array.</returns>
    /// <exception cref="SmtPartialException">The lambda is not well-formed, or has several variables.</exception>
    public SmtArrayValue OfLambda(IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth)
    {
        if (items.Count != 3)
            throw m_meter.Doubt();

        PayForVariables(items[1]);
        if (!SmtTerms.TrySortedVariables(items[1], out var names, out var sorts) || names.Length == 0)
            throw m_meter.Doubt();

        // An array of several indices is a sort the evaluator has no values for.
        if (names.Length != 1)
            throw m_meter.Undecided(SmtValidationReasons.UNSUPPORTED_SORT);

        var indexSort = m_run.Resolve(sorts[0]);
        if (indexSort.Kind == SmtSortKind.Unsupported)
            throw m_meter.Undecided(indexSort.Reason ?? SmtValidationReasons.UNSUPPORTED_SORT);

        var map = TryFiniteMap(names[0], items[2], scope, indexSort, null, depth);
        if (map != null)
            return map;

        // The body is kept with a copy of the names around it: it is evaluated when the array is read.
        var captured = scope?.Capture() ?? new SmtScope(false);
        m_meter.Charge(1 + captured.Count);
        return new SmtArrayValue(indexSort, null, new SmtArraySource(names[0], items[2], captured), SmtArrayValue.NO_ENTRIES);
    }

    /// <summary>
    /// Reads an array over a function at an index nothing was stored at.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="key">The index, already in the array's index sort.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The function is not evaluated at the index.</exception>
    public SmtValue Read(SmtArrayValue array, SmtValue key, int depth)
    {
        var source = array.Source!;
        SmtValue value;
        if (source.Function != null)
        {
            value = m_run.Evaluator.Call(source.Function, [key], depth);
        }
        else
        {
            // The lambda's own names are borrowed for one evaluation and left as they were,
            // whatever becomes of it: the array is read again later. The variable is bound and
            // taken back under its name at every read, which is paid for by the name's length.
            var scope = source.Scope!;
            var mark = scope.Mark;
            m_meter.ChargeName(source.Parameter!, SmtScope.BIND_READS);
            scope.Bind(source.Parameter!, key);
            try
            {
                value = m_run.Evaluator.Eval(source.Body!, scope, depth + SmtEvaluator.CALL_LEVELS);
            }
            finally
            {
                scope.Unwind(mark);
            }
        }

        return array.ElementSort == null ? value : m_run.Conformance.Conform(value, array.ElementSort, depth);
    }

    /// <summary>
    /// Reads a function's body as a finite map, when it is the chain a solver prints.
    /// </summary>
    /// <returns>The array, or null when the body is anything else.</returns>
    private SmtArrayValue? TryFiniteMap(string parameter, SmtExpression body, SmtScope? scope, SmtSort indexSort, SmtSort? elementSort, int depth)
    {
        var cases = new List<(SmtExpression Index, SmtExpression Value)>();
        var otherwise = body;
        while (otherwise.IsApplication(SmtVocabulary.ITE) && otherwise.Items.Count == 4)
        {
            m_meter.Charge(1);
            var condition = otherwise.Items[1];
            if (!condition.IsApplication(EQUAL) || condition.Items.Count != 3)
                return null;

            SmtExpression index;
            if (IsNamed(condition.Items[1], parameter))
                index = condition.Items[2];
            else if (IsNamed(condition.Items[2], parameter))
                index = condition.Items[1];
            else
                return null;

            if (Mentions(index, parameter) || Mentions(otherwise.Items[2], parameter))
                return null;

            cases.Add((index, otherwise.Items[2]));
            otherwise = otherwise.Items[3];
        }

        if (Mentions(otherwise, parameter))
            return null;

        var value = m_run.Evaluator.Eval(otherwise, scope, depth + SmtEvaluator.CALL_LEVELS);
        if (elementSort != null)
            value = m_run.Conformance.Conform(value, elementSort, depth);

        // The first condition that holds wins: the cases are stored from the last to the first.
        SmtValue array = new SmtArrayValue(indexSort, elementSort, value, SmtArrayValue.NO_ENTRIES);
        for (var index = cases.Count - 1; index >= 0; index--)
        {
            var key = m_run.Evaluator.Eval(cases[index].Index, scope, depth + SmtEvaluator.CALL_LEVELS);
            array = m_arrays.Store(array, key, m_run.Evaluator.Eval(cases[index].Value, scope, depth + SmtEvaluator.CALL_LEVELS), depth);
        }

        return (SmtArrayValue)array;
    }

    /// <summary>
    /// Whether a term mentions a name anywhere, bound or not. Walked with a stack of its own:
    /// a term may nest deeper than calls should.
    /// </summary>
    private bool Mentions(SmtExpression term, string name)
    {
        if (!term.IsList)
            return IsNamed(term, name);

        var pending = new Stack<SmtExpression>();
        pending.Push(term);
        while (pending.Count > 0)
        {
            var items = pending.Pop().Items;
            m_meter.Charge(1 + items.Count);
            foreach (var item in items)
            {
                if (item.IsList)
                    pending.Push(item);
                else if (IsNamed(item, name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a term is the symbol of a name. Names of two lengths are told apart at once; two
    /// names of one length are compared character by character, which is paid for by the length.
    /// </summary>
    private bool IsNamed(SmtExpression term, string name)
    {
        if (term.Kind != SmtExpressionKind.Symbol || term.Text.Length != name.Length)
            return false;

        m_meter.ChargeName(name);
        return string.Equals(term.Text, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pays for reading the names of a lambda's variables, before they are read: each is checked
    /// and told apart from the others as text.
    /// </summary>
    private void PayForVariables(SmtExpression variables)
    {
        if (!variables.IsList)
            return;

        foreach (var variable in variables.Items)
        {
            if (variable.IsList && variable.Items.Count > 0)
                m_meter.ChargeName(variable.Items[0].Text, SmtTerms.BINDER_READS);
        }
    }

    #endregion
}
