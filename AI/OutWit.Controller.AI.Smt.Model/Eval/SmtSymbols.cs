using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a name stands for in one validation. A term of the problem sees the local names around
/// it, then the names the problem introduces. A term of the model sees, after those, the names
/// the model adds for itself: the elements of its universes, listed or only named, and the
/// functions behind its arrays. A term of the problem never sees the model's own names.
/// <para>
/// A symbol the problem declares takes its value from the model, checked against the declared
/// sort. A constant the model leaves out takes the value the answer of <c>get-value</c> reports
/// for it, when there is one and that answer is taken; any other symbol the model leaves out
/// takes the default value of its sort, and the validation remembers that it did
/// (<see cref="SmtMeter.Complete"/>).
/// </para>
/// <para>
/// Every read of a symbol the problem declares or defines is counted
/// (<see cref="SmtMeter.Refer"/>), also where a value that was written with one is read again
/// from memory: a value a solver reports is written without them.
/// </para>
/// </summary>
internal sealed class SmtSymbols
{
    #region Constants

    /// <summary>
    /// How many times the name of a constant is read as text when its value is computed and
    /// remembered: the model's definitions, the reported values or the model's elements, the
    /// name itself where it is taken for an element nobody lists, and the table that keeps the
    /// value.
    /// </summary>
    private const int FIRST_READS = 5;

    /// <summary>
    /// How many times the name of a function is read as text when the model's definition of it
    /// is found and remembered: the model's definitions, and the table that keeps the function.
    /// </summary>
    private const int FUNCTION_READS = 2;

    private static readonly string[] NO_NAMES = [];

    #endregion

    #region Fields

    private readonly SmtRun m_run;
    private readonly SmtMeter m_meter;
    private readonly Dictionary<string, (SmtValue? Value, bool Completed, bool Referred, SmtPartialException? Failure)> m_constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (SmtValue? Value, bool Completed, bool Referred, SmtPartialException? Failure)> m_modelConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtFunction> m_interpretations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SmtFunction> m_modelFunctions = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the names of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    public SmtSymbols(SmtRun run)
    {
        m_run = run;
        m_meter = run.Meter;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The value of a name that stands alone.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="scope">The local names around it, or null.</param>
    /// <param name="depth">The level of nesting the name is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The name has no value the evaluator computes.</exception>
    public SmtValue Constant(string name, SmtScope? scope, int depth)
    {
        // Every table the name is looked up in reads the whole name: each lookup is paid for
        // by the name's length before it is made, here and in what this calls.
        if (scope != null)
        {
            m_meter.ChargeName(name);
            if (scope.TryGet(name, out var local))
                return local;
        }

        if (SmtVocabulary.IsTruthValue(name))
            return SmtBoolValue.Of(string.Equals(name, SmtVocabulary.TRUE, StringComparison.Ordinal));

        m_meter.ChargeName(name);
        if (m_run.Problem.Symbols.TryGetValue(name, out var symbol))
        {
            if (symbol.Kind is SmtSymbolKind.Declared or SmtSymbolKind.Defined)
                m_meter.Refer();

            return Recall(m_constants, name, symbol, depth);
        }

        return scope is { IsModel: true } ? Recall(m_modelConstants, name, null, depth) : throw Unknown(name);
    }

    /// <summary>
    /// Applies a name of the problem or of the model to values.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="arguments">The values of the arguments, at least one.</param>
    /// <param name="inModel">Whether the application stands in the model's text.</param>
    /// <param name="depth">The level of nesting the application is at.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application is not evaluated to a value.</exception>
    public SmtValue Apply(string name, SmtValue[] arguments, bool inModel, int depth)
    {
        m_meter.ChargeName(name);
        if (!m_run.Problem.Symbols.TryGetValue(name, out var symbol))
            return inModel && TryGetModelFunction(name, out var added) ? m_run.Evaluator.Call(added, arguments, depth) : throw Unknown(name);

        switch (symbol.Kind)
        {
            case SmtSymbolKind.Defined:
                m_meter.Refer();
                return m_run.Evaluator.Call(symbol.Function!, arguments, depth);
            case SmtSymbolKind.Declared:
                m_meter.Refer();
                return m_run.Evaluator.Call(Interpretation(name, symbol), arguments, depth);
            case SmtSymbolKind.Constructor:
                return Construct(symbol.Constructor!, arguments, depth);
            case SmtSymbolKind.Selector when arguments.Length == 1:
                return Select(symbol, arguments[0]);
            case SmtSymbolKind.Tester when arguments.Length == 1:
                return Test(symbol.Constructor!, arguments[0]);
            default:
                throw m_meter.Doubt();
        }
    }

    /// <summary>
    /// Whether a value was built by a constructor: <c>((_ is C) x)</c>.
    /// </summary>
    /// <param name="constructor">The constructor's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The truth value.</returns>
    /// <exception cref="SmtPartialException">The name is no constructor, or the value is of another datatype.</exception>
    public SmtValue Test(string constructor, SmtValue value)
    {
        m_meter.ChargeName(constructor);
        if (!m_run.Problem.Symbols.TryGetValue(constructor, out var symbol) || symbol.Kind != SmtSymbolKind.Constructor)
            throw m_meter.Doubt();

        return Test(symbol.Constructor!, value);
    }

    /// <summary>
    /// Finds the function of one argument or more a name stands for: the model's interpretation
    /// of a symbol the problem declares, or, in the model's text, a function the model added.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="inModel">Whether the name stands in the model's text.</param>
    /// <param name="function">The function.</param>
    /// <returns>True when the name stands for such a function.</returns>
    /// <exception cref="SmtPartialException">The model's definition does not fit the declaration, or the step budget is used up.</exception>
    public bool TryGetFunction(string name, bool inModel, out SmtFunction function)
    {
        function = null!;
        m_meter.ChargeName(name);
        if (!m_run.Problem.Symbols.TryGetValue(name, out var symbol))
            return inModel && TryGetModelFunction(name, out function);

        if (symbol.Kind != SmtSymbolKind.Declared || symbol.ParameterSorts!.Count == 0)
            return false;

        m_meter.Refer();
        function = Interpretation(name, symbol);
        return true;
    }

    /// <summary>
    /// The value of a name of the problem, or of a name the model added, computed once. The
    /// value is remembered with whether it rests on a symbol the model left out, and with
    /// whether it was written with a symbol of the problem, so that every later reader of it is
    /// known to rest on the same; a name that is not evaluated is remembered with the reason.
    /// </summary>
    private SmtValue Recall(Dictionary<string, (SmtValue? Value, bool Completed, bool Referred, SmtPartialException? Failure)> known, string name, SmtSymbol? symbol, int depth)
    {
        m_meter.ChargeName(name);
        if (known.TryGetValue(name, out var entry))
        {
            if (entry.Failure != null)
                throw new SmtPartialException(entry.Failure.Kind, entry.Failure.Reason, entry.Failure.Detail, depth);

            if (entry.Completed)
                m_meter.Complete();

            if (entry.Referred)
                m_meter.Refer();

            return entry.Value!;
        }

        m_meter.ChargeName(name, FIRST_READS);
        var before = m_meter.Completions;
        var references = m_meter.References;
        try
        {
            var value = symbol != null ? OfProblem(name, symbol, depth) : OfModel(name, depth);
            known[name] = (value, m_meter.Completions != before, m_meter.References != references, null);
            return value;
        }
        catch (SmtPartialException exception) when (exception.Kind != SmtPartialKind.Terminal)
        {
            known[name] = (null, false, false, exception);
            throw;
        }
    }

    private SmtValue OfProblem(string name, SmtSymbol symbol, int depth)
    {
        switch (symbol.Kind)
        {
            case SmtSymbolKind.Defined when symbol.Function!.Arity == 0:
                var defined = m_run.Evaluator.Eval(symbol.Function.Body!, null, depth + SmtEvaluator.CALL_LEVELS);
                return symbol.Function.ResultSort == null ? defined : m_run.Conformance.Conform(defined, symbol.Function.ResultSort, depth);
            case SmtSymbolKind.Declared when symbol.ParameterSorts!.Count == 0:
                return OfDeclared(name, symbol.ResultSort!, depth);
            case SmtSymbolKind.Constructor when symbol.Constructor!.Arity == 0:
                return Construct(symbol.Constructor, [], depth);
            default:
                throw m_meter.Doubt();
        }
    }

    private SmtValue OfDeclared(string name, SmtSort sort, int depth)
    {
        if (m_run.Model.Functions.TryGetValue(name, out var defined))
        {
            // Defined with parameters where the problem declares a constant: not what a solver prints.
            if (defined.Parameters.Count != 0)
                throw m_meter.Doubt();

            return m_run.Conformance.Conform(m_run.Evaluator.Eval(defined.Body, new SmtScope(true), depth + SmtEvaluator.CALL_LEVELS), sort, depth);
        }

        // The model's own text comes first: a reported value never stands in for a definition.
        // Where the model leaves the constant out, the solver completed its model when it was
        // asked for the value, and that value is the constant's.
        if (m_run.TakesValues && m_run.Model.Completions.TryGetValue(name, out var reported))
            return m_run.Conformance.Conform(m_run.Evaluator.Eval(reported, new SmtScope(true), depth + SmtEvaluator.CALL_LEVELS), sort, depth);

        m_meter.Complete();
        return m_run.Conformance.Default(sort, depth);
    }

    private SmtValue OfModel(string name, int depth)
    {
        if (m_run.Model.Elements.TryGetValue(name, out var declaration))
        {
            var sort = m_run.Resolve(declaration.Items[3]);
            return sort.Kind == SmtSortKind.Uninterpreted ? new SmtElementValue(name, sort) : throw m_meter.Doubt();
        }

        if (!m_run.Model.Functions.TryGetValue(name, out var function))
            return Unlisted(name) ?? throw Unknown(name);

        if (function.Parameters.Count != 0)
            throw Unknown(name);

        var value = m_run.Evaluator.Eval(function.Body, new SmtScope(true), depth + SmtEvaluator.CALL_LEVELS);
        return m_run.Conformance.Conform(value, m_run.Resolve(function.ResultSort), depth);
    }

    /// <summary>
    /// An element of a sort the model names without listing the sort's universe, read as the
    /// closed script declares it (<see cref="SmtElementNames"/>): the name is a sort's, the mark
    /// and a number; the problem declares that sort without parameters; and nobody declares the
    /// name. It is an element of the sort like a listed one, the same element exactly when the
    /// name is the same.
    /// </summary>
    /// <returns>The element, or null when the name is no such element's.</returns>
    private SmtElementValue? Unlisted(string name)
    {
        if (!SmtElementNames.TrySortOf(name, out var sort) || !m_run.Problem.Resolver.IsDeclaredWithoutParameters(sort))
            return null;

        return new SmtElementValue(name, m_run.Resolve(SmtExpression.Symbol(sort)));
    }

    /// <summary>
    /// The function a declared symbol stands for under the model: the model's definition when it
    /// fits the declaration, a function without a body when the model leaves the symbol out.
    /// </summary>
    private SmtFunction Interpretation(string name, SmtSymbol symbol)
    {
        m_meter.ChargeName(name);
        if (m_interpretations.TryGetValue(name, out var known))
            return known;

        m_meter.ChargeName(name, FUNCTION_READS);
        var sorts = symbol.ParameterSorts!;
        SmtFunction function;
        if (!m_run.Model.Functions.TryGetValue(name, out var defined))
        {
            function = new SmtFunction(NO_NAMES, sorts, symbol.ResultSort!, null, false);
        }
        else
        {
            // The solver prints the declared sorts back; a definition with others is not one of this symbol.
            if (defined.Parameters.Count != sorts.Count || !ReferenceEquals(m_run.Resolve(defined.ResultSort), symbol.ResultSort))
                throw m_meter.Doubt();

            for (var index = 0; index < sorts.Count; index++)
            {
                if (!ReferenceEquals(m_run.Resolve(defined.ParameterSorts[index]), sorts[index]))
                    throw m_meter.Doubt();
            }

            function = new SmtFunction(defined.Parameters, sorts, symbol.ResultSort!, defined.Body, true);
        }

        m_interpretations[name] = function;
        return function;
    }

    private bool TryGetModelFunction(string name, out SmtFunction function)
    {
        m_meter.ChargeName(name);
        if (m_modelFunctions.TryGetValue(name, out function!))
            return true;

        m_meter.ChargeName(name, FUNCTION_READS);
        if (!m_run.Model.Functions.TryGetValue(name, out var defined) || defined.Parameters.Count == 0)
            return false;

        var sorts = new SmtSort[defined.Parameters.Count];
        for (var index = 0; index < sorts.Length; index++)
            sorts[index] = m_run.Resolve(defined.ParameterSorts[index]);

        function = new SmtFunction(defined.Parameters, sorts, m_run.Resolve(defined.ResultSort), defined.Body, true);
        m_modelFunctions[name] = function;
        return true;
    }

    /// <summary>
    /// Builds a datatype value. The fields of a datatype without parameters are checked against
    /// their sorts here, so such a value is of its sort from the start; a value of a datatype
    /// with parameters is checked where a sort is declared for it.
    /// </summary>
    private SmtValue Construct(SmtConstructor constructor, SmtValue[] arguments, int depth)
    {
        if (arguments.Length != constructor.Arity)
            throw m_meter.Doubt();

        m_meter.Charge(1 + arguments.Length);
        if (constructor.FieldSorts != null)
        {
            for (var index = 0; index < arguments.Length; index++)
                arguments[index] = m_run.Conformance.Conform(arguments[index], constructor.FieldSorts[index], depth);
        }

        return new SmtDatatypeValue(constructor, arguments) { ConformsTo = constructor.Datatype.Sort };
    }

    private SmtValue Select(SmtSymbol selector, SmtValue value)
    {
        if (value is not SmtDatatypeValue built || !ReferenceEquals(built.Constructor.Datatype, selector.Constructor!.Datatype))
            throw m_meter.Doubt();

        // The standard leaves a selector on a value of another constructor to the model.
        return ReferenceEquals(built.Constructor, selector.Constructor)
            ? built.Fields[selector.Field]
            : throw m_meter.Undecided(SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR);
    }

    private SmtValue Test(SmtConstructor constructor, SmtValue value)
    {
        if (value is not SmtDatatypeValue built || !ReferenceEquals(built.Constructor.Datatype, constructor.Datatype))
            throw m_meter.Doubt();

        return SmtBoolValue.Of(ReferenceEquals(built.Constructor, constructor));
    }

    private SmtPartialException Unknown(string name)
    {
        return SmtVocabulary.TryGetUnsupported(name, out var reason, out var detail)
            ? m_meter.Undecided(reason, detail)
            : m_meter.Doubt(SmtValidationReasons.UNKNOWN_SYMBOL);
    }

    #endregion
}
