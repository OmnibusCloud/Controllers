using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Evaluates closed terms under a model: substitution and computation, nothing searched. It
/// walks a term by calls, one level of nesting a call, counted against the ceiling
/// (<see cref="SmtMeter.Descend"/>); the places where real terms nest without end (a chain of
/// <c>let</c>, of <c>ite</c>, of <c>and</c>, <c>or</c> or <c>=&gt;</c>, an annotated term, the
/// body a <c>match</c> picks) are walked by going round instead, so they cost no nesting at all.
/// <para>
/// A term that is not evaluated to a value throws <see cref="SmtPartialException"/>. Nothing
/// else leaves here: every shape a term may lack is checked before it is read.
/// </para>
/// </summary>
internal sealed class SmtEvaluator
{
    #region Constants

    /// <summary>
    /// The levels of nesting a call takes: the body of a function, the term a symbol stands
    /// for, the function behind an array. Each stands on about twice the frames a term in a
    /// term does, and the ceiling is a count of frames in the end.
    /// </summary>
    public const int CALL_LEVELS = 2;

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly SmtConnectives m_connectives;
    private readonly SmtMatch m_match;
    private readonly SmtIdentifiers m_identifiers;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the evaluator of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtEvaluator(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
        m_connectives = new SmtConnectives(run, this);
        m_match = new SmtMatch(run, this);
        m_identifiers = new SmtIdentifiers(run, this);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Evaluates a term to a value.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="scope">The local names it stands among; null at the top of an assertion.</param>
    /// <param name="depth">The level of nesting the term is at: 1 for an assertion's own term.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The term is not evaluated to a value.</exception>
    public SmtValue Eval(SmtExpression term, SmtScope? scope, int depth)
    {
        m_meter.Descend(depth);

        var mark = scope?.Mark ?? 0;
        var local = scope;
        var value = Reduce(term, ref local, depth, out var asTruth);

        // What a let or a match of this term bound in the caller's scope ends with the term.
        // A scope the term made for itself is simply dropped.
        if (scope != null && scope.Mark != mark)
            scope.Unwind(mark);

        // The last argument of a connective was walked in the connective's place: it has to
        // be a truth value, as every argument of a connective.
        if (asTruth && value.Kind != SmtValueKind.Bool)
            throw m_meter.Doubt();

        return value;
    }

    /// <summary>
    /// Evaluates a term that must be of sort Bool.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="scope">The local names it stands among, or null.</param>
    /// <param name="depth">The level of nesting the term is at.</param>
    /// <returns>Its truth.</returns>
    /// <exception cref="SmtPartialException">The term is not evaluated, or is no Bool.</exception>
    public bool Truth(SmtExpression term, SmtScope? scope, int depth)
    {
        return Eval(term, scope, depth) is SmtBoolValue truth ? truth.Value : throw m_meter.Doubt();
    }

    /// <summary>
    /// Applies a name to values: an operator of the theories, or a name of the problem or of the model.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="arguments">The values of the arguments; the call may replace them by the same values in the sorts the name declares.</param>
    /// <param name="inModel">Whether the application stands in the model's text.</param>
    /// <param name="depth">The level of nesting the application is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application is not evaluated to a value.</exception>
    public SmtValue Apply(string name, SmtValue[] arguments, bool inModel, int depth)
    {
        if (arguments.Length == 0)
            throw m_meter.Doubt();

        return SmtVocabulary.TryGetOperator(name, out var known)
            ? m_run.Operators.Apply(known, arguments, [], depth)
            : m_run.Symbols.Apply(name, arguments, inModel, depth);
    }

    /// <summary>
    /// Calls a function on values: each argument checked against the sort of its parameter,
    /// the result against the sort the function declares.
    /// </summary>
    /// <param name="function">The function.</param>
    /// <param name="arguments">The values of the arguments.</param>
    /// <param name="depth">The level of nesting the call is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The call is not evaluated to a value.</exception>
    public SmtValue Call(SmtFunction function, SmtValue[] arguments, int depth)
    {
        if (arguments.Length != function.Arity)
            throw m_meter.Doubt();

        m_meter.Charge(1 + arguments.Length);
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = m_run.Conformance.Conform(arguments[index], function.ParameterSorts[index], depth);

        if (function.Body == null)
        {
            // A symbol the model left out: any function of the declared sorts would do, and
            // the one that gives the default value everywhere is taken.
            m_meter.Complete();
            return m_run.Conformance.Default(function.ResultSort!, depth);
        }

        // A parameter is written into the scope under its name: that reads the whole name, and is paid for by its length.
        var scope = new SmtScope(function.IsModel);
        for (var index = 0; index < arguments.Length; index++)
        {
            m_meter.ChargeName(function.Parameters[index]);
            scope.Define(function.Parameters[index], arguments[index]);
        }

        var value = Eval(function.Body, scope, depth + CALL_LEVELS);
        return function.ResultSort == null ? value : m_run.Conformance.Conform(value, function.ResultSort, depth);
    }

    /// <summary>
    /// Evaluates the arguments of an application.
    /// </summary>
    /// <param name="items">The application's items.</param>
    /// <param name="first">The position of the first argument.</param>
    /// <param name="scope">The local names the application stands among, or null.</param>
    /// <param name="depth">The level of nesting the application is at.</param>
    /// <returns>The values, in order.</returns>
    /// <exception cref="SmtPartialException">An argument is not evaluated to a value.</exception>
    public SmtValue[] Arguments(IReadOnlyList<SmtExpression> items, int first, SmtScope? scope, int depth)
    {
        var arguments = new SmtValue[items.Count - first];
        for (var index = 0; index < arguments.Length; index++)
            arguments[index] = Eval(items[index + first], scope, depth + 1);

        return arguments;
    }

    /// <summary>
    /// Walks a term down to a value. A form whose value is the value of one of its parts goes
    /// round with that part instead of calling for it: the branch an ite picks, the body of a
    /// let, an annotated term, the case a match picks, the last argument of a connective.
    /// </summary>
    private SmtValue Reduce(SmtExpression term, ref SmtScope? scope, int depth, out bool asTruth)
    {
        asTruth = false;
        while (true)
        {
            m_meter.Charge(1);
            if (!term.IsList)
                return Atom(term, scope, depth);

            var items = term.Items;
            if (items.Count == 0)
                throw m_meter.Doubt();

            var head = items[0];
            if (head.IsList)
                return m_identifiers.Apply(items, scope, depth);

            if (head.Kind != SmtExpressionKind.Symbol)
                throw m_meter.Doubt();

            // A reserved word is the word only when it is not written between bars; between
            // them the same name is an ordinary symbol, which no problem can declare here.
            var reserved = !head.IsQuoted;
            switch (head.Text)
            {
                case SmtVocabulary.LET when reserved:
                    term = Let(items, ref scope, depth);
                    continue;
                case SmtVocabulary.ANNOTATION when reserved && items.Count >= 2:
                    term = items[1];
                    continue;
                case SmtVocabulary.MATCH when reserved:
                    term = m_match.Choose(items, ref scope, depth);
                    continue;
                case SmtVocabulary.FORALL when reserved:
                case SmtVocabulary.EXISTS when reserved:
                    throw m_meter.Undecided(SmtValidationReasons.QUANTIFIER);
                case SmtVocabulary.INDEXED when reserved:
                    return m_identifiers.Indexed(items, scope, depth);
                case SmtVocabulary.AS when reserved:
                    return m_identifiers.Qualified(items, scope, depth);
                case SmtVocabulary.ITE:
                    if (items.Count != 4)
                        throw m_meter.Doubt();

                    // Only the branch the condition picks is evaluated.
                    term = Truth(items[1], scope, depth + 1) ? items[2] : items[3];
                    continue;
                case SmtVocabulary.AND:
                case SmtVocabulary.OR:
                case SmtVocabulary.IMPLIES:
                    var rest = m_connectives.Decide(head.Text, items, scope, depth, out var decided);
                    if (rest == null)
                        return SmtBoolValue.Of(decided);

                    asTruth = true;
                    term = rest;
                    continue;
                case SmtVocabulary.LAMBDA:
                    return m_run.Arrays.Lambda(items, scope, depth);
                case SmtVocabulary.ROOT_OBJECT:
                    throw m_meter.Undecided(SmtValidationReasons.ALGEBRAIC_NUMBER);
                default:
                    return Apply(head.Text, Arguments(items, 1, scope, depth), scope?.IsModel ?? false, depth);
            }
        }
    }

    private SmtValue Atom(SmtExpression term, SmtScope? scope, int depth)
    {
        return term.Kind switch
        {
            SmtExpressionKind.Symbol => m_run.Symbols.Constant(term.Text, scope, depth),
            SmtExpressionKind.Numeral => SmtLiterals.Numeral(term.Text, m_meter),
            SmtExpressionKind.Decimal => SmtLiterals.Decimal(term.Text, m_meter),
            SmtExpressionKind.Hexadecimal => SmtLiterals.Hexadecimal(term.Text, m_meter),
            SmtExpressionKind.Binary => SmtLiterals.Binary(term.Text, m_meter),
            SmtExpressionKind.String => SmtLiterals.String(term.Text, m_meter),
            _ => throw m_meter.Doubt()
        };
    }

    /// <summary>
    /// Binds the variables of a let, all of them at once: every value is computed among the
    /// names outside the let, and only the body sees the new ones.
    /// </summary>
    /// <returns>The body, to go on with.</returns>
    private SmtExpression Let(IReadOnlyList<SmtExpression> items, ref SmtScope? scope, int depth)
    {
        if (items.Count != 3 || !items[1].IsList || items[1].Items.Count == 0)
            throw m_meter.Doubt();

        var bindings = items[1].Items;
        var names = new string[bindings.Count];
        var values = new SmtValue[bindings.Count];
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (!binding.IsList || binding.Items.Count != 2 || !SmtTerms.IsSymbol(binding.Items[0]))
                throw m_meter.Doubt();

            // The name is read as text when it is checked and told apart from the other names
            // of the let, and again when it is bound and taken back: paid for by its length.
            m_meter.ChargeName(binding.Items[0].Text, SmtTerms.BINDER_READS + SmtScope.BIND_READS);
            if (!SmtTerms.CanBind(binding.Items[0]))
                throw m_meter.Doubt();

            names[index] = binding.Items[0].Text;
            values[index] = Eval(binding.Items[1], scope, depth + 1);
        }

        if (!SmtTerms.AreDistinct(names))
            throw m_meter.Doubt();

        scope ??= new SmtScope(false);
        for (var index = 0; index < names.Length; index++)
            scope.Bind(names[index], values[index]);

        return items[2];
    }

    #endregion
}
