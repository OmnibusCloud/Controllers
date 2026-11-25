using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The arrays of one validation: the constant array, <c>select</c>, <c>store</c>, a function of
/// the model taken as an array (<c>(_ as-array f)</c>), <c>lambda</c>, and the comparison of
/// two arrays.
/// <para>
/// Two finite maps are compared index by index over the indices either of them stores. Where
/// those all agree and the two defaults differ, the arrays differ exactly when the index sort
/// has an index neither of them stores: certain for Int, Real and String, counted for
/// bit-vectors and Bool, and not decided for the other sorts, whose size the model sets.
/// </para>
/// </summary>
internal sealed class SmtArrays
{
    #region Constants

    private const int BOOL_VALUES = 2;

    /// <summary>A bit-vector sort this wide has more indices than any array stores.</summary>
    private const int COUNTABLE_WIDTH = 62;

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly SmtArrayFunctions m_functions;
    private readonly Dictionary<string, (SmtValue Value, bool Completed, bool Referred)> m_ofFunctions = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the arrays of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtArrays(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
        m_functions = new SmtArrayFunctions(run, this);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The array that holds one value at every index: <c>((as const (Array I E)) v)</c>.
    /// </summary>
    /// <param name="sort">The array's sort, as written.</param>
    /// <param name="value">The value.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The array.</returns>
    /// <exception cref="SmtPartialException">The sort is no array sort, or the value is not of its element sort.</exception>
    public SmtValue Constant(SmtExpression sort, SmtValue value, int depth)
    {
        var resolved = m_run.Resolve(sort);
        if (resolved.Kind == SmtSortKind.Unsupported)
            throw m_meter.Undecided(resolved.Reason ?? SmtValidationReasons.UNSUPPORTED_SORT);

        if (resolved.Kind != SmtSortKind.Array)
            throw m_meter.Doubt();

        return new SmtArrayValue(resolved.Index!, resolved.Element, m_run.Conformance.Conform(value, resolved.Element!, depth), SmtArrayValue.NO_ENTRIES);
    }

    /// <summary>
    /// The value of an array at an index.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="index">The index.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The first value is no array, or the index is not of its index sort.</exception>
    public SmtValue Select(SmtValue array, SmtValue index, int depth)
    {
        if (array is not SmtArrayValue read)
            throw m_meter.Doubt();

        var key = Key(read, index, depth);
        if (read.Entries.TryGetValue(key, out var stored))
            return stored;

        return read.Default ?? m_functions.Read(read, key, depth);
    }

    /// <summary>
    /// The array that differs from another at one index.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="index">The index.</param>
    /// <param name="value">The value at the index.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The new array.</returns>
    /// <exception cref="SmtPartialException">The first value is no array, or the index or the value is not of its sorts.</exception>
    public SmtValue Store(SmtValue array, SmtValue index, SmtValue value, int depth)
    {
        if (array is not SmtArrayValue written)
            throw m_meter.Doubt();

        var key = Key(written, index, depth);
        if (written.ElementSort != null)
            value = m_run.Conformance.Conform(value, written.ElementSort, depth);

        var entries = written.Entries.SetItem(key, value);
        m_meter.CheckArrayEntries(entries.Count);
        return written.WithEntries(entries);
    }

    /// <summary>
    /// A function taken as an array: <c>(_ as-array f)</c>. The model's interpretation of a
    /// declared function, or in the model's text a function the model added.
    /// </summary>
    /// <param name="name">The function's name.</param>
    /// <param name="inModel">Whether the term stands in the model's text.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The array.</returns>
    /// <exception cref="SmtPartialException">The name is no function of one argument.</exception>
    public SmtValue OfFunction(string name, bool inModel, int depth)
    {
        // Asked first, every time: a term of the problem does not see a function the model added,
        // whether or not the model's own text has already taken it as an array.
        if (!m_run.Symbols.TryGetFunction(name, inModel, out var function))
            throw m_meter.Doubt(SmtValidationReasons.UNKNOWN_SYMBOL);

        // The arrays made so far are kept by the function's name: finding one reads the name,
        // and so does keeping a new one below.
        m_meter.ChargeName(name);
        if (m_ofFunctions.TryGetValue(name, out var known))
        {
            if (known.Completed)
                m_meter.Complete();

            // What the function's body read of the problem's symbols when the array was made, it reads for every later reader too.
            if (known.Referred)
                m_meter.Refer();

            return known.Value;
        }

        // An array of several indices is a sort the evaluator has no values for.
        if (function.Arity != 1)
            throw m_meter.Undecided(SmtValidationReasons.UNSUPPORTED_SORT);

        m_meter.ChargeName(name);
        var before = m_meter.Completions;
        var references = m_meter.References;
        var value = m_functions.ToArray(function, depth);
        m_ofFunctions[name] = (value, m_meter.Completions != before, m_meter.References != references);
        return value;
    }

    /// <summary>
    /// The array a <c>lambda</c> of one variable stands for.
    /// </summary>
    /// <param name="items">The lambda's items: the word, the variable with its sort, the body.</param>
    /// <param name="scope">The local names the lambda stands among, or null.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The array.</returns>
    /// <exception cref="SmtPartialException">The lambda is not well-formed, or has several variables.</exception>
    public SmtValue Lambda(IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth)
    {
        return m_functions.OfLambda(items, scope, depth);
    }

    /// <summary>
    /// Whether two arrays hold the same value at every index.
    /// </summary>
    /// <param name="left">One array.</param>
    /// <param name="right">The other.</param>
    /// <param name="depth">The level of nesting the comparison is made at.</param>
    /// <returns>True for the same array.</returns>
    /// <exception cref="SmtPartialException">The arrays are of two sorts, or their comparison is not decided.</exception>
    public bool Same(SmtArrayValue left, SmtArrayValue right, int depth)
    {
        if (!ReferenceEquals(left.IndexSort, right.IndexSort))
            throw m_meter.Doubt();

        if (left.ElementSort != null && right.ElementSort != null && !ReferenceEquals(left.ElementSort, right.ElementSort))
            throw m_meter.Doubt();

        if (ReferenceEquals(left, right))
            return true;

        // An array known only as a function can be read, not compared.
        if (left.Default == null || right.Default == null)
            throw m_meter.Undecided(SmtValidationReasons.ARRAY_COMPARISON);

        m_meter.Charge(1 + (long)left.Entries.Count + right.Entries.Count);
        long stored = left.Entries.Count;
        foreach (var entry in left.Entries)
        {
            ChargeLookup(entry.Key, right.Entries.Count);
            var other = right.Entries.TryGetValue(entry.Key, out var found) ? found : right.Default;
            if (!m_run.Equality.Same(entry.Value, other, depth + 1))
                return false;
        }

        foreach (var entry in right.Entries)
        {
            ChargeLookup(entry.Key, left.Entries.Count);
            if (left.Entries.ContainsKey(entry.Key))
                continue;

            stored++;
            if (!m_run.Equality.Same(left.Default, entry.Value, depth + 1))
                return false;
        }

        if (m_run.Equality.Same(left.Default, right.Default, depth + 1))
            return true;

        // Every stored index agrees and the defaults differ: the arrays differ at an index
        // that is not stored, when there is one. They are the same only when there is none.
        return IsExhausted(left.IndexSort, stored);
    }

    /// <summary>
    /// Checks an index against an array and makes it a key of the array's map. A value that is
    /// an array, or holds one, is no key: two arrays can be the same without being written the same.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="index">The index.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>The index, in the array's index sort.</returns>
    /// <exception cref="SmtPartialException">The index is not of the index sort, or is no key.</exception>
    public SmtValue Key(SmtArrayValue array, SmtValue index, int depth)
    {
        var key = m_run.Conformance.Conform(index, array.IndexSort, depth);
        if (key.HoldsArray)
            throw m_meter.Undecided(SmtValidationReasons.UNSUPPORTED_SORT);

        if (key.Depth > SmtValueComparer.MAX_DEPTH)
            throw m_meter.TooDeep();

        ChargeLookup(key, array.Entries.Count);
        return key;
    }

    /// <summary>
    /// Pays for finding a key in a map: it is compared with about the logarithm of the stored indices.
    /// </summary>
    private void ChargeLookup(SmtValue key, int stored)
    {
        var comparisons = 2 + (long)Math.Log2(stored + 1);
        m_meter.Charge(key.KeyCost > long.MaxValue / comparisons ? long.MaxValue : key.KeyCost * comparisons);
    }

    /// <summary>
    /// Whether a sort has no index beyond a number of different ones.
    /// </summary>
    private bool IsExhausted(SmtSort indexSort, long stored)
    {
        switch (indexSort.Kind)
        {
            case SmtSortKind.Int:
            case SmtSortKind.Real:
            case SmtSortKind.String:
                return false;
            case SmtSortKind.Bool:
                return stored >= BOOL_VALUES;
            case SmtSortKind.BitVector:
                return indexSort.Width <= COUNTABLE_WIDTH && stored >= 1L << indexSort.Width;
            default:
                throw m_meter.Undecided(SmtValidationReasons.ARRAY_COMPARISON);
        }
    }

    #endregion
}
