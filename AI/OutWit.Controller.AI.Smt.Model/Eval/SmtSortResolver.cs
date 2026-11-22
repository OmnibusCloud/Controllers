using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Reads a sort as written into the sort it is: the theory sorts the evaluator has values for,
/// the sorts, aliases and datatypes a problem declares, and for everything else a sort without
/// values, with the reason. A sort that holds a sort without values is one itself.
/// <para>
/// An alias is expanded once for each list of arguments and remembered, so aliases that use
/// each other twice a level cost what they are worth, not two to the power of their depth.
/// </para>
/// </summary>
internal sealed class SmtSortResolver
{
    #region Constants

    /// <summary>
    /// How deep a sort may nest before it is a sort without values: a sort in a sort counts a
    /// level, and so does an alias that is expanded. Real sorts nest a few levels.
    /// </summary>
    public const int MAX_NESTING = 128;

    /// <summary>The widest bit-vector sort there is a name for: no budget lets a value be this wide.</summary>
    private const int MAX_WIDTH = 1 << 30;

    private const string BOOL = "Bool";
    private const string INT = "Int";
    private const string REAL = "Real";
    private const string STRING = "String";
    private const string ARRAY = "Array";
    private const string BIT_VECTOR = "BitVec";
    private const string FLOATING_POINT = "FloatingPoint";
    private const char ALIAS_TAG = 'L';
    private const char FIELDS_TAG = 'F';

    private static readonly HashSet<string> REGULAR_EXPRESSION_SORTS = new(StringComparer.Ordinal) { "RegLan", "RegEx" };

    private static readonly HashSet<string> FLOATING_POINT_SORTS = new(StringComparer.Ordinal)
    {
        FLOATING_POINT, "Float16", "Float32", "Float64", "Float128", "RoundingMode"
    };

    private static readonly SmtSort[] NO_SORTS = [];

    #endregion

    #region Fields

    private readonly Dictionary<string, (int Ordinal, int Arity)> m_declared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtSortAlias> m_aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtDatatype> m_datatypes = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Whether a name already names a sort: one of the theories', or one the problem gave.
    /// </summary>
    /// <param name="name">A sort's name.</param>
    /// <returns>True when a new sort cannot take the name.</returns>
    public bool IsTaken(string name)
    {
        return name is BOOL or INT or REAL or STRING or ARRAY or BIT_VECTOR
               || REGULAR_EXPRESSION_SORTS.Contains(name) || FLOATING_POINT_SORTS.Contains(name)
               || m_declared.ContainsKey(name) || m_aliases.ContainsKey(name) || m_datatypes.ContainsKey(name);
    }

    /// <summary>
    /// Adds a sort the problem leaves to the model (<c>declare-sort</c>).
    /// </summary>
    /// <param name="name">The sort's name.</param>
    /// <param name="arity">How many sort arguments it takes.</param>
    public void Declare(string name, int arity)
    {
        m_declared[name] = (m_declared.Count, arity);
    }

    /// <summary>
    /// Adds an alias (<c>define-sort</c>).
    /// </summary>
    /// <param name="name">The alias's name.</param>
    /// <param name="parameters">The names of its parameters.</param>
    /// <param name="body">The sort it stands for, as written.</param>
    public void Define(string name, IReadOnlyList<string> parameters, SmtExpression body)
    {
        m_aliases[name] = new SmtSortAlias(m_aliases.Count, parameters, body);
    }

    /// <summary>
    /// Adds a datatype.
    /// </summary>
    /// <param name="datatype">The datatype.</param>
    public void Declare(SmtDatatype datatype)
    {
        m_datatypes[datatype.Name] = datatype;
    }

    /// <summary>
    /// Reads a sort as written.
    /// </summary>
    /// <param name="expression">The sort as written.</param>
    /// <param name="table">Where the sort is kept.</param>
    /// <param name="meter">Who pays for the work; null while a problem is read, which its own size bounds.</param>
    /// <param name="parameters">The sorts of the parameters in scope, or null.</param>
    /// <returns>The sort; a sort without values when the evaluator has none for it.</returns>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public SmtSort Resolve(SmtExpression expression, SmtSortTable table, SmtMeter? meter, IReadOnlyDictionary<string, SmtSort>? parameters = null)
    {
        return Resolve(expression, table, meter, parameters, 1);
    }

    /// <summary>
    /// The sorts of a constructor's fields for one sort of its datatype: the declaration's
    /// parameters replaced by the sort's arguments.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <param name="sort">A sort of the constructor's datatype.</param>
    /// <param name="table">Where the sorts are kept.</param>
    /// <param name="meter">Who pays for the work.</param>
    /// <returns>One sort for each field.</returns>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public IReadOnlyList<SmtSort> FieldSorts(SmtConstructor constructor, SmtSort sort, SmtSortTable table, SmtMeter? meter)
    {
        if (constructor.FieldSorts != null)
            return constructor.FieldSorts;

        var key = SmtSortTable.Key(FIELDS_TAG, constructor.Ordinal, [sort]);
        if (table.TryRecall(key, out var known))
            return known;

        var datatype = constructor.Datatype;
        var parameters = new Dictionary<string, SmtSort>(StringComparer.Ordinal);
        for (var index = 0; index < datatype.Parameters.Count && index < sort.Arguments.Count; index++)
            parameters[datatype.Parameters[index]] = sort.Arguments[index];

        var sorts = new SmtSort[constructor.Arity];
        for (var index = 0; index < sorts.Length; index++)
            sorts[index] = Resolve(constructor.FieldSortExpressions[index], table, meter, parameters, 1);

        table.Remember(key, sorts);
        return sorts;
    }

    private SmtSort Resolve(SmtExpression expression, SmtSortTable table, SmtMeter? meter, IReadOnlyDictionary<string, SmtSort>? parameters, int nesting)
    {
        meter?.Charge(1);
        if (nesting > MAX_NESTING)
            return table.Unsupported(SmtValidationReasons.UNSUPPORTED_SORT);

        if (expression.Kind == SmtExpressionKind.Symbol)
        {
            if (parameters != null && parameters.TryGetValue(expression.Text, out var bound))
                return bound;

            return Named(expression.Text, NO_SORTS, table, meter, nesting);
        }

        if (!expression.IsList || expression.Items.Count < 2 || expression.Items[0].Kind != SmtExpressionKind.Symbol)
            return table.Unsupported(SmtValidationReasons.UNSUPPORTED_SORT);

        if (SmtTerms.IsReserved(expression.Items[0], SmtVocabulary.INDEXED))
            return Indexed(expression, table);

        var arguments = new SmtSort[expression.Items.Count - 1];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = Resolve(expression.Items[index + 1], table, meter, parameters, nesting + 1);
            if (arguments[index].Kind == SmtSortKind.Unsupported)
                return arguments[index];
        }

        return Named(expression.Items[0].Text, arguments, table, meter, nesting);
    }

    private SmtSort Named(string name, SmtSort[] arguments, SmtSortTable table, SmtMeter? meter, int nesting)
    {
        switch (name)
        {
            case BOOL when arguments.Length == 0:
                return table.Bool;
            case INT when arguments.Length == 0:
                return table.Int;
            case REAL when arguments.Length == 0:
                return table.Real;
            case STRING when arguments.Length == 0:
                return table.String;
            case ARRAY when arguments.Length == 2:
                return table.Array(arguments[0], arguments[1]);
        }

        if (REGULAR_EXPRESSION_SORTS.Contains(name))
            return table.Unsupported(SmtValidationReasons.REGULAR_EXPRESSION);

        if (FLOATING_POINT_SORTS.Contains(name))
            return table.Unsupported(SmtValidationReasons.FLOATING_POINT);

        if (m_aliases.TryGetValue(name, out var alias) && alias.Parameters.Count == arguments.Length)
            return Expand(alias, arguments, table, meter, nesting);

        if (m_datatypes.TryGetValue(name, out var datatype) && datatype.Parameters.Count == arguments.Length)
            return table.Datatype(datatype, arguments);

        if (m_declared.TryGetValue(name, out var declared) && declared.Arity == arguments.Length)
            return table.Uninterpreted(declared.Ordinal, arguments);

        return table.Unsupported(SmtValidationReasons.UNSUPPORTED_SORT);
    }

    private SmtSort Expand(SmtSortAlias alias, SmtSort[] arguments, SmtSortTable table, SmtMeter? meter, int nesting)
    {
        var key = SmtSortTable.Key(ALIAS_TAG, alias.Ordinal, arguments);
        if (table.TryRecall(key, out var known))
            return known[0];

        var parameters = new Dictionary<string, SmtSort>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index++)
            parameters[alias.Parameters[index]] = arguments[index];

        var sort = Resolve(alias.Body, table, meter, parameters, nesting + 1);
        table.Remember(key, [sort]);
        return sort;
    }

    private static SmtSort Indexed(SmtExpression expression, SmtSortTable table)
    {
        var items = expression.Items;
        if (items.Count == 3 && items[1].IsSymbol(BIT_VECTOR) && SmtTerms.TryIndex(items[2], out var width) && width is >= 1 and <= MAX_WIDTH)
            return table.BitVector(width);

        if (items[1].IsSymbol(FLOATING_POINT))
            return table.Unsupported(SmtValidationReasons.FLOATING_POINT);

        return table.Unsupported(SmtValidationReasons.UNSUPPORTED_SORT);
    }

    #endregion

    #region Properties

    /// <summary>How many datatypes the problem has declared so far.</summary>
    public int Datatypes => m_datatypes.Count;

    #endregion
}
