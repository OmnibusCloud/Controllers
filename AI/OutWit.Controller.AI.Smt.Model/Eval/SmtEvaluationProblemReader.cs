using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Reads the commands of a problem into what the evaluator needs (<see cref="SmtEvaluationProblem"/>). It
/// takes the commands a sanitized problem keeps: <c>set-logic</c>, <c>set-info</c>,
/// declarations and definitions of sorts, datatypes and functions, and <c>assert</c>. It never
/// throws: a command it cannot make sense of, any other command, or a name with two meanings
/// makes the problem one no model can be checked against, with the reason.
/// <para>
/// Nothing here recurses on the nesting of a term: the search for named terms keeps its own
/// stack, and a sort is read to a fixed depth.
/// </para>
/// </summary>
internal sealed class SmtEvaluationProblemReader
{
    #region Constants

    private const string SET_LOGIC = "set-logic";
    private const string SET_INFO = "set-info";
    private const string DECLARE_SORT = "declare-sort";
    private const string DEFINE_SORT = "define-sort";
    private const string DECLARE_CONST = "declare-const";
    private const string DECLARE_FUN = "declare-fun";
    private const string DEFINE_FUN = "define-fun";
    private const string DEFINE_FUN_REC = "define-fun-rec";
    private const string DEFINE_FUNS_REC = "define-funs-rec";
    private const string DECLARE_DATATYPE = "declare-datatype";
    private const string DECLARE_DATATYPES = "declare-datatypes";
    private const string ASSERT = "assert";

    private static readonly string[] NO_NAMES = [];
    private static readonly SmtSort[] NO_SORTS = [];

    #endregion

    #region Fields

    private readonly Dictionary<string, SmtSymbol> m_symbols = new(StringComparer.Ordinal);
    private readonly List<string> m_declared = [];
    private readonly List<SmtExpression> m_assertions = [];
    private string? m_fault;

    #endregion

    #region Functions

    /// <summary>
    /// Reads a problem.
    /// </summary>
    /// <param name="commands">The problem's commands, in order.</param>
    /// <returns>What the problem says; with a fault when no model of it can be checked.</returns>
    public static SmtEvaluationProblem Read(IReadOnlyList<SmtExpression> commands)
    {
        var reader = new SmtEvaluationProblemReader();
        foreach (var command in commands)
            reader.ReadCommand(command);

        return new SmtEvaluationProblem(reader.Sorts, reader.Resolver, reader.m_symbols, reader.m_declared, reader.m_assertions, reader.m_fault);
    }

    /// <summary>
    /// Records why no model of the problem can be checked. The first reason stays.
    /// </summary>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    public void Fail(string reason)
    {
        m_fault ??= reason;
    }

    /// <summary>
    /// Gives a name its meaning. A name that already has one, from the problem or from the
    /// theories, makes the problem ambiguous: solvers tell such meanings apart by the sorts of
    /// the arguments, the evaluator by the name alone.
    /// </summary>
    /// <param name="name">The name as written.</param>
    /// <param name="symbol">What it stands for.</param>
    /// <returns>True when the name was free.</returns>
    public bool TryAdd(SmtExpression name, SmtSymbol symbol)
    {
        if (name.Kind != SmtExpressionKind.Symbol)
        {
            Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return false;
        }

        return TryAdd(name.Text, symbol);
    }

    /// <summary>
    /// Gives a name its meaning (<see cref="TryAdd(SmtExpression, SmtSymbol)"/>).
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="symbol">What it stands for.</param>
    /// <returns>True when the name was free.</returns>
    public bool TryAdd(string name, SmtSymbol symbol)
    {
        // A general reserved word can only be declared between bars, where the standard has it
        // an ordinary symbol and solvers are known to read the word all the same.
        if (SmtVocabulary.IsOperator(name) || SmtReservedWords.IsGeneral(name) || !m_symbols.TryAdd(name, symbol))
        {
            Fail(SmtValidationReasons.AMBIGUOUS_SYMBOL);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a list of names that differ from each other: the parameters of a sort.
    /// </summary>
    /// <param name="expression">The list as written.</param>
    /// <param name="names">The names.</param>
    /// <returns>True for such a list.</returns>
    public static bool TryNames(SmtExpression expression, out string[] names)
    {
        names = NO_NAMES;
        if (!expression.IsList)
            return false;

        var read = new string[expression.Items.Count];
        for (var index = 0; index < read.Length; index++)
        {
            if (!SmtTerms.IsSymbol(expression.Items[index]))
                return false;

            read[index] = expression.Items[index].Text;
        }

        if (!SmtTerms.AreDistinct(read))
            return false;

        names = read;
        return true;
    }

    private void ReadCommand(SmtExpression? command)
    {
        if (command == null || !command.IsList || command.Items.Count == 0 || !SmtTerms.IsSymbol(command.Items[0]) || command.Items[0].IsQuoted)
        {
            Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        var items = command.Items;
        switch (items[0].Text)
        {
            case SET_LOGIC:
            case SET_INFO:
                break;
            case DECLARE_SORT:
                DeclareSort(items);
                break;
            case DEFINE_SORT:
                DefineSort(items);
                break;
            case DECLARE_CONST when items.Count == 3:
                Declare(items[1], [], items[2]);
                break;
            case DECLARE_FUN when items.Count == 4 && items[2].IsList:
                Declare(items[1], items[2].Items, items[3]);
                break;
            case DEFINE_FUN when items.Count == 5:
            case DEFINE_FUN_REC when items.Count == 5:
                Define(items[1], items[2], items[3], items[4]);
                break;
            case DEFINE_FUNS_REC when items.Count == 3 && items[1].IsList && items[2].IsList && items[1].Items.Count == items[2].Items.Count:
                DefineTogether(items[1].Items, items[2].Items);
                break;
            case DECLARE_DATATYPE when items.Count == 3:
                SmtDatatypeReader.DeclareOne(this, items[1], items[2]);
                break;
            case DECLARE_DATATYPES when items.Count == 3 && items[1].IsList && items[2].IsList:
                SmtDatatypeReader.DeclareTogether(this, items[1].Items, items[2].Items);
                break;
            case ASSERT when items.Count == 2:
                m_assertions.Add(items[1]);
                FindNamedTerms(items[1], false);
                break;
            default:
                Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
                break;
        }
    }

    private void DeclareSort(IReadOnlyList<SmtExpression> items)
    {
        // Solvers take the arity as optional; the standard asks for it.
        var arity = 0;
        if (items.Count is < 2 or > 3 || !SmtTerms.IsSymbol(items[1]) || (items.Count == 3 && !SmtTerms.TryIndex(items[2], out arity)))
        {
            Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        if (Resolver.IsTaken(items[1].Text))
        {
            Fail(SmtValidationReasons.AMBIGUOUS_SYMBOL);
            return;
        }

        Resolver.Declare(items[1].Text, arity);
    }

    private void DefineSort(IReadOnlyList<SmtExpression> items)
    {
        if (items.Count != 4 || !SmtTerms.IsSymbol(items[1]) || !TryNames(items[2], out var parameters))
        {
            Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        if (Resolver.IsTaken(items[1].Text))
        {
            Fail(SmtValidationReasons.AMBIGUOUS_SYMBOL);
            return;
        }

        Resolver.Define(items[1].Text, parameters, items[3]);
    }

    private void Declare(SmtExpression name, IReadOnlyList<SmtExpression> parameterSorts, SmtExpression resultSort)
    {
        var sorts = parameterSorts.Count == 0 ? NO_SORTS : new SmtSort[parameterSorts.Count];
        for (var index = 0; index < sorts.Length; index++)
            sorts[index] = Resolver.Resolve(parameterSorts[index], Sorts, null);

        if (TryAdd(name, SmtSymbol.Declared(sorts, Resolver.Resolve(resultSort, Sorts, null))))
            m_declared.Add(name.Text);
    }

    private void Define(SmtExpression name, SmtExpression parameters, SmtExpression resultSort, SmtExpression body)
    {
        if (!SmtTerms.TrySortedVariables(parameters, out var names, out var sortsAsWritten))
        {
            Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        var sorts = new SmtSort[sortsAsWritten.Length];
        for (var index = 0; index < sorts.Length; index++)
            sorts[index] = Resolver.Resolve(sortsAsWritten[index], Sorts, null);

        var function = new SmtFunction(names, sorts, Resolver.Resolve(resultSort, Sorts, null), body, false);
        TryAdd(name, SmtSymbol.Defined(function));
        FindNamedTerms(body, names.Length > 0);
    }

    private void DefineTogether(IReadOnlyList<SmtExpression> declarations, IReadOnlyList<SmtExpression> bodies)
    {
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (!declaration.IsList || declaration.Items.Count != 3)
            {
                Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
                return;
            }

            Define(declaration.Items[0], declaration.Items[1], declaration.Items[2], bodies[index]);
        }
    }

    /// <summary>
    /// Finds every <c>(! t :named n)</c> of a term and makes n the name of t. A term named
    /// under a binder may use the binder's variables, which the name alone does not carry:
    /// such a name is known and not evaluated.
    /// </summary>
    private void FindNamedTerms(SmtExpression term, bool underBinder)
    {
        var pending = new Stack<(SmtExpression Term, bool UnderBinder)>();
        pending.Push((term, underBinder));
        while (pending.Count > 0)
        {
            var (current, bound) = pending.Pop();
            if (!current.IsList || current.Items.Count == 0)
                continue;

            var items = current.Items;
            var annotated = SmtTerms.IsReserved(items[0], SmtVocabulary.ANNOTATION) && items.Count >= 2;
            if (annotated)
                NameTerm(items, bound);

            var binds = IsBinder(items[0]);
            for (var index = 0; index < items.Count; index++)
            {
                // What stands after the annotated term is attributes, not the term.
                var inside = bound || binds || (annotated && index > 1);
                pending.Push((items[index], inside));
            }
        }
    }

    private void NameTerm(IReadOnlyList<SmtExpression> items, bool underBinder)
    {
        for (var index = 2; index + 1 < items.Count; index++)
        {
            if (items[index].Kind != SmtExpressionKind.Keyword || !string.Equals(items[index].Text, SmtVocabulary.NAMED, StringComparison.Ordinal))
                continue;

            var symbol = underBinder ? SmtSymbol.Unevaluated() : SmtSymbol.Defined(new SmtFunction(NO_NAMES, NO_SORTS, null, items[1], false));
            TryAdd(items[index + 1], symbol);
        }
    }

    private static bool IsBinder(SmtExpression head)
    {
        if (head.Kind != SmtExpressionKind.Symbol)
            return false;

        if (string.Equals(head.Text, SmtVocabulary.LAMBDA, StringComparison.Ordinal))
            return true;

        return !head.IsQuoted && head.Text is SmtVocabulary.LET or SmtVocabulary.FORALL or SmtVocabulary.EXISTS or SmtVocabulary.MATCH;
    }

    #endregion

    #region Properties

    /// <summary>The problem's sorts, as they are met.</summary>
    public SmtSortTable Sorts { get; } = new();

    /// <summary>How a sort as written is read, as the problem declares its own.</summary>
    public SmtSortResolver Resolver { get; } = new();

    #endregion
}
