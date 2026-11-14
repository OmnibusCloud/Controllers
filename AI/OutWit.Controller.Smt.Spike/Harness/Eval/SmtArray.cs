namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// An array value: a base and the stores over it. The base is either a constant (every index
/// maps to the default: <c>((as const (Array I E)) v)</c>) or a function of the index (a model's
/// <c>(_ as-array f)</c> or <c>lambda</c>). An array over a constant base is a finite map and can
/// be compared; one over a function can be read but not compared (partial).
/// </summary>
internal sealed class SmtArray : SmtValue
{
    #region Fields

    private readonly Dictionary<string, (SmtValue Index, SmtValue Value)> m_entries;

    #endregion

    #region Constructors

    private SmtArray(SmtValue? defaultValue, Func<SmtValue, SmtValue>? function, Dictionary<string, (SmtValue Index, SmtValue Value)> entries)
    {
        Default = defaultValue;
        Function = function;
        m_entries = entries;
    }

    #endregion

    #region Functions

    /// <summary>The array that maps every index to one value.</summary>
    /// <param name="defaultValue">The value.</param>
    /// <returns>The constant array.</returns>
    public static SmtArray Constant(SmtValue defaultValue)
    {
        return new SmtArray(defaultValue, null, []);
    }

    /// <summary>The array whose content is a function of the index.</summary>
    /// <param name="function">Reads one index.</param>
    /// <returns>The array.</returns>
    public static SmtArray OfFunction(Func<SmtValue, SmtValue> function)
    {
        return new SmtArray(null, function, []);
    }

    /// <summary>The array with one index set (an SMT-LIB store).</summary>
    /// <param name="index">The index.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new array.</returns>
    public SmtArray Store(SmtValue index, SmtValue value)
    {
        var entries = new Dictionary<string, (SmtValue, SmtValue)>(m_entries) { [index.Key()] = (index, value) };
        return new SmtArray(Default, Function, entries);
    }

    /// <summary>The value at an index (an SMT-LIB select).</summary>
    /// <param name="index">The index.</param>
    /// <returns>The value.</returns>
    public SmtValue Select(SmtValue index)
    {
        if (m_entries.TryGetValue(index.Key(), out var entry))
            return entry.Value;
        if (Default != null)
            return Default;
        return Function!(index);
    }

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        if (other is not SmtArray array)
            return false;
        if (Default == null || array.Default == null)
            throw new SmtPartialException("array equality over a function");

        foreach (var key in m_entries.Keys.Union(array.m_entries.Keys))
        {
            var index = m_entries.TryGetValue(key, out var mine) ? mine.Index : array.m_entries[key].Index;
            if (!Select(index).SameAs(array.Select(index)))
                return false;
        }

        // Indices without a store: the defaults. (An index sort of one element would make two
        // different defaults equal arrays; the spike does not reason about cardinalities.)
        return Default.SameAs(array.Default);
    }

    /// <inheritdoc />
    public override string Key()
    {
        if (Default == null)
            throw new SmtPartialException("array over a function as an index");

        var entries = m_entries.Values.Where(e => !e.Value.SameAs(Default)).Select(e => $"{e.Index.Key()}->{e.Value.Key()}").OrderBy(k => k, StringComparer.Ordinal);
        return $"a:[{Default.Key()}|{string.Join(",", entries)}]";
    }

    #endregion

    #region Properties

    /// <summary>The value of every index not stored, for an array over a constant; else null.</summary>
    public SmtValue? Default { get; }

    /// <summary>The function under the stores, for an array over a function; else null.</summary>
    public Func<SmtValue, SmtValue>? Function { get; }

    #endregion
}
