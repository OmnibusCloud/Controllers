using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The identifiers that are more than a symbol: indexed ones (<c>(_ bv5 8)</c>,
/// <c>(_ extract 7 0)</c>, <c>(_ is cons)</c>, <c>(_ as-array f)</c>) and ones qualified with a
/// sort (<c>(as nil (List Int))</c>, <c>(as const (Array Int Int))</c>), as a term of their own
/// and at the head of an application.
/// </summary>
internal sealed class SmtIdentifiers
{
    #region Constants

    /// <summary>The most indices an operator takes: the two positions of <c>extract</c>.</summary>
    private const int MAX_INDICES = 2;

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly SmtEvaluator m_evaluator;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the reader of identifiers of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    /// <param name="evaluator">The walker of terms.</param>
    public SmtIdentifiers(SmtRun run, SmtEvaluator evaluator)
    {
        m_run = run;
        m_meter = run.Meter;
        m_evaluator = evaluator;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Evaluates an indexed identifier that is a term: a bit-vector value, or a function as an array.
    /// </summary>
    /// <param name="items">The identifier's items, the underscore first.</param>
    /// <param name="scope">The local names it stands among, or null.</param>
    /// <param name="depth">The level of nesting it is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The identifier is no value the evaluator has.</exception>
    public SmtValue Indexed(IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth)
    {
        if (items.Count < 3 || !SmtTerms.IsSymbol(items[1]))
            throw m_meter.Doubt();

        // The name may be a value written in digits, which are looked at one by one before the
        // value is read: paid for by the name's length.
        var name = items[1].Text;
        m_meter.ChargeName(name);
        if (items.Count == 3 && string.Equals(name, SmtVocabulary.AS_ARRAY, StringComparison.Ordinal) && SmtTerms.IsSymbol(items[2]))
            return m_run.Arrays.OfFunction(items[2].Text, scope?.IsModel ?? false, depth);

        if (items.Count == 3 && IsBitVectorValue(name) && SmtTerms.TryIndex(items[2], out var width))
            return SmtLiterals.BitVector(name.AsSpan(SmtVocabulary.BIT_VECTOR_VALUE_PREFIX.Length), width, m_meter);

        throw Unknown(name);
    }

    /// <summary>
    /// Evaluates <c>(as name sort)</c>: the name, checked against the sort.
    /// </summary>
    /// <param name="items">The identifier's items, the word first.</param>
    /// <param name="scope">The local names it stands among, or null.</param>
    /// <param name="depth">The level of nesting it is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The name has no value, or not one of the sort.</exception>
    public SmtValue Qualified(IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth)
    {
        if (items.Count != 3 || !SmtTerms.IsSymbol(items[1]))
            throw m_meter.Doubt();

        var value = m_run.Symbols.Constant(items[1].Text, scope, depth);
        return m_run.Conformance.Conform(value, m_run.Resolve(items[2]), depth);
    }

    /// <summary>
    /// Evaluates an application whose head is an identifier with indices or with a sort.
    /// </summary>
    /// <param name="items">The application's items, the identifier first.</param>
    /// <param name="scope">The local names it stands among, or null.</param>
    /// <param name="depth">The level of nesting it is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application is not evaluated to a value.</exception>
    public SmtValue Apply(IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth)
    {
        var head = items[0].Items;
        if (items.Count < 2 || head.Count < 3 || !SmtTerms.IsSymbol(head[1]))
            throw m_meter.Doubt();

        var name = head[1].Text;
        var inModel = scope?.IsModel ?? false;
        if (SmtTerms.IsReserved(head[0], SmtVocabulary.AS) && head.Count == 3)
        {
            var arguments = m_evaluator.Arguments(items, 1, scope, depth);
            if (string.Equals(name, SmtVocabulary.CONST_ARRAY, StringComparison.Ordinal))
                return arguments.Length == 1 ? m_run.Arrays.Constant(head[2], arguments[0], depth) : throw m_meter.Doubt();

            var value = m_evaluator.Apply(name, arguments, inModel, depth);
            return m_run.Conformance.Conform(value, m_run.Resolve(head[2]), depth);
        }

        if (!SmtTerms.IsReserved(head[0], SmtVocabulary.INDEXED))
            throw m_meter.Doubt();

        if (string.Equals(name, SmtVocabulary.IS, StringComparison.Ordinal))
        {
            if (head.Count != 3 || !SmtTerms.IsSymbol(head[2]) || items.Count != 2)
                throw m_meter.Doubt();

            return m_run.Symbols.Test(head[2].Text, m_evaluator.Eval(items[1], scope, depth + 1));
        }

        if (!SmtVocabulary.TryGetIndexedOperator(name, out var known))
            throw Unknown(name);

        // No operator takes more indices than MAX_INDICES. A longer list is read to the end all
        // the same, to find an index that is no numeral: it pays a step an index for that,
        // before it is read.
        if (head.Count - 2 > MAX_INDICES)
            m_meter.Charge(head.Count - 2);

        var indices = new int[head.Count - 2];
        for (var index = 0; index < indices.Length; index++)
        {
            if (!SmtTerms.TryIndex(head[index + 2], out indices[index]))
                throw m_meter.Doubt();
        }

        return m_run.Operators.Apply(known, m_evaluator.Arguments(items, 1, scope, depth), indices, depth);
    }

    private SmtPartialException Unknown(string name)
    {
        return SmtVocabulary.TryGetUnsupported(name, out var reason, out var detail)
            ? m_meter.Undecided(reason, detail)
            : m_meter.Doubt(SmtValidationReasons.UNKNOWN_SYMBOL);
    }

    /// <summary>
    /// Whether a name is <c>bv</c> and digits: the value of a bit-vector written in decimal.
    /// </summary>
    private static bool IsBitVectorValue(string name)
    {
        var prefix = SmtVocabulary.BIT_VECTOR_VALUE_PREFIX.Length;
        if (name.Length <= prefix || !name.StartsWith(SmtVocabulary.BIT_VECTOR_VALUE_PREFIX, StringComparison.Ordinal))
            return false;

        for (var index = prefix; index < name.Length; index++)
        {
            if (!char.IsAsciiDigit(name[index]))
                return false;
        }

        return true;
    }

    #endregion
}
