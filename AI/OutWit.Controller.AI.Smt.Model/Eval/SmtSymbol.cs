namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// One name a problem introduces, and what it stands for. Immutable once the problem is read.
/// </summary>
internal sealed class SmtSymbol
{
    #region Constructors

    private SmtSymbol(SmtSymbolKind kind)
    {
        Kind = kind;
    }

    #endregion

    #region Functions

    /// <summary>
    /// A constant or a function the problem declares.
    /// </summary>
    /// <param name="parameterSorts">The sort of each argument; none for a constant.</param>
    /// <param name="resultSort">The sort of the result.</param>
    /// <returns>The symbol.</returns>
    public static SmtSymbol Declared(IReadOnlyList<SmtSort> parameterSorts, SmtSort resultSort)
    {
        return new SmtSymbol(SmtSymbolKind.Declared) { ParameterSorts = parameterSorts, ResultSort = resultSort };
    }

    /// <summary>
    /// A function the problem defines, or a term it names.
    /// </summary>
    /// <param name="function">The definition.</param>
    /// <returns>The symbol.</returns>
    public static SmtSymbol Defined(SmtFunction function)
    {
        return new SmtSymbol(SmtSymbolKind.Defined) { Function = function };
    }

    /// <summary>
    /// A name the evaluator cannot evaluate.
    /// </summary>
    /// <returns>The symbol.</returns>
    public static SmtSymbol Unevaluated()
    {
        return new SmtSymbol(SmtSymbolKind.Unevaluated);
    }

    /// <summary>
    /// A constructor.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>The symbol.</returns>
    public static SmtSymbol OfConstructor(SmtConstructor constructor)
    {
        return new SmtSymbol(SmtSymbolKind.Constructor) { Constructor = constructor };
    }

    /// <summary>
    /// The selector of a field.
    /// </summary>
    /// <param name="constructor">The constructor the field belongs to.</param>
    /// <param name="field">The field's position.</param>
    /// <returns>The symbol.</returns>
    public static SmtSymbol Selector(SmtConstructor constructor, int field)
    {
        return new SmtSymbol(SmtSymbolKind.Selector) { Constructor = constructor, Field = field };
    }

    /// <summary>
    /// The tester of a constructor.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>The symbol.</returns>
    public static SmtSymbol Tester(SmtConstructor constructor)
    {
        return new SmtSymbol(SmtSymbolKind.Tester) { Constructor = constructor };
    }

    #endregion

    #region Properties

    /// <summary>What the name stands for.</summary>
    public SmtSymbolKind Kind { get; }

    /// <summary>The sorts of a declared symbol's arguments; null for any other symbol.</summary>
    public IReadOnlyList<SmtSort>? ParameterSorts { get; private init; }

    /// <summary>The sort of a declared symbol's result; null for any other symbol.</summary>
    public SmtSort? ResultSort { get; private init; }

    /// <summary>The definition of a defined symbol; null for any other symbol.</summary>
    public SmtFunction? Function { get; private init; }

    /// <summary>The constructor of a constructor, a selector or a tester; null for any other symbol.</summary>
    public SmtConstructor? Constructor { get; private init; }

    /// <summary>The position of a selector's field.</summary>
    public int Field { get; private init; }

    #endregion
}
