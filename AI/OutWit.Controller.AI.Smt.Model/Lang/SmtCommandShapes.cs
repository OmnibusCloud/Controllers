namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The shape of every kept command, as the standard writes it: how many parts it has and what
/// stands where a name must stand. A sort or a term is any s-expression here: what they mean is
/// the solver's to check. While checking, the names a command declares are gathered: the name
/// of a constant, a function or a sort, of a datatype, of its constructors and its selectors.
/// <para>
/// A name is a symbol that is no reserved word (a reserved word between bars is a symbol).
/// </para>
/// </summary>
internal sealed class SmtCommandShapes
{
    #region Constants

    private const string PARAMETRIC = "par";
    private const int PAIR = 2;

    private static readonly Dictionary<string, string> SHAPES = new(StringComparer.Ordinal)
    {
        [SmtCommandPolicy.SET_LOGIC] = "(set-logic <symbol>)",
        [SmtCommandPolicy.SET_INFO] = "(set-info <keyword>) or (set-info <keyword> <value>)",
        [SmtCommandPolicy.DECLARE_SORT] = "(declare-sort <symbol>) or (declare-sort <symbol> <numeral>)",
        [SmtCommandPolicy.DEFINE_SORT] = "(define-sort <symbol> (<symbol>*) <sort>)",
        [SmtCommandPolicy.DECLARE_CONST] = "(declare-const <symbol> <sort>)",
        [SmtCommandPolicy.DECLARE_FUN] = "(declare-fun <symbol> (<sort>*) <sort>)",
        [SmtCommandPolicy.DEFINE_FUN] = "(define-fun <symbol> ((<symbol> <sort>)*) <sort> <term>)",
        [SmtCommandPolicy.DEFINE_FUN_REC] = "(define-fun-rec <symbol> ((<symbol> <sort>)*) <sort> <term>)",
        [SmtCommandPolicy.DEFINE_FUNS_REC] = "(define-funs-rec ((<symbol> ((<symbol> <sort>)*) <sort>)+) (<term>+)), with one term for each function",
        [SmtCommandPolicy.DECLARE_DATATYPE] = "(declare-datatype <symbol> <datatype>), where <datatype> is ((<symbol> (<symbol> <sort>)*)+) or (par (<symbol>+) ((<symbol> (<symbol> <sort>)*)+))",
        [SmtCommandPolicy.DECLARE_DATATYPES] = "(declare-datatypes ((<symbol> <numeral>)+) (<datatype>+)), with one <datatype> for each name, where <datatype> is ((<symbol> (<symbol> <sort>)*)+) or (par (<symbol>+) ((<symbol> (<symbol> <sort>)*)+))",
        [SmtCommandPolicy.ASSERT] = "(assert <term>)"
    };

    #endregion

    #region Fields

    private readonly List<SmtExpression> m_names = [];
    private bool m_reservedWord;

    #endregion

    #region Functions

    /// <summary>
    /// Checks the shape of a kept command and gathers the names it declares (<see cref="Names"/>).
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="head">Its name: one of the kept commands.</param>
    /// <returns>What is wrong with the shape, in words; null when the command is well-formed.</returns>
    public string? Check(SmtExpression command, string head)
    {
        m_names.Clear();
        m_reservedWord = false;

        var items = command.Items;
        var fits = head switch
        {
            SmtCommandPolicy.SET_LOGIC => items.Count == 2 && IsName(items[1]),
            SmtCommandPolicy.SET_INFO => items.Count is 2 or 3 && items[1].Kind == SmtExpressionKind.Keyword,
            SmtCommandPolicy.DECLARE_SORT => items.Count is 2 or 3 && Declares(items[1]) && (items.Count == 2 || items[2].Kind == SmtExpressionKind.Numeral),
            SmtCommandPolicy.DEFINE_SORT => items.Count == 4 && Declares(items[1]) && IsListOf(items[2], IsName, mayBeEmpty: true),
            SmtCommandPolicy.DECLARE_CONST => items.Count == 3 && Declares(items[1]),
            SmtCommandPolicy.DECLARE_FUN => items.Count == 4 && Declares(items[1]) && items[2].IsList,
            SmtCommandPolicy.DEFINE_FUN or SmtCommandPolicy.DEFINE_FUN_REC => items.Count == 5 && Declares(items[1]) && IsListOf(items[2], IsSortedName, mayBeEmpty: true),
            SmtCommandPolicy.DEFINE_FUNS_REC => items.Count == 3 && IsListOf(items[1], DeclaresFunction, mayBeEmpty: false) && items[2].IsList && items[2].Items.Count == items[1].Items.Count,
            SmtCommandPolicy.DECLARE_DATATYPE => items.Count == 3 && Declares(items[1]) && DeclaresDatatype(items[2]),
            SmtCommandPolicy.DECLARE_DATATYPES => items.Count == 3 && IsListOf(items[1], DeclaresArity, mayBeEmpty: false) && IsListOf(items[2], DeclaresDatatype, mayBeEmpty: false) && items[2].Items.Count == items[1].Items.Count,
            SmtCommandPolicy.ASSERT => items.Count == 2,
            _ => false
        };

        if (fits)
            return null;

        m_names.Clear();

        var shape = SHAPES.GetValueOrDefault(head, "");
        return m_reservedWord
            ? $"a reserved word stands where a name must stand; the command is written {shape}"
            : $"the command is written {shape}";
    }

    /// <summary>
    /// A name: a symbol that is no reserved word. Between bars a reserved word is a symbol.
    /// </summary>
    private bool IsName(SmtExpression expression)
    {
        if (expression.Kind != SmtExpressionKind.Symbol)
            return false;

        if (expression.IsQuoted || !SmtReservedWords.Contains(expression.Text))
            return true;

        m_reservedWord = true;
        return false;
    }

    private bool Declares(SmtExpression expression)
    {
        if (!IsName(expression))
            return false;

        m_names.Add(expression);
        return true;
    }

    /// <summary>A sorted variable: <c>(name sort)</c>. The name is bound, not declared.</summary>
    private bool IsSortedName(SmtExpression expression)
    {
        return expression.IsList && expression.Items.Count == PAIR && IsName(expression.Items[0]);
    }

    /// <summary>The declaration of one function of <c>define-funs-rec</c>: <c>(name ((name sort)*) sort)</c>.</summary>
    private bool DeclaresFunction(SmtExpression expression)
    {
        return expression.IsList && expression.Items.Count == 3 && Declares(expression.Items[0]) && IsListOf(expression.Items[1], IsSortedName, mayBeEmpty: true);
    }

    /// <summary>The name and the arity of one datatype of <c>declare-datatypes</c>: <c>(name numeral)</c>.</summary>
    private bool DeclaresArity(SmtExpression expression)
    {
        return expression.IsList && expression.Items.Count == PAIR && Declares(expression.Items[0]) && expression.Items[1].Kind == SmtExpressionKind.Numeral;
    }

    /// <summary>
    /// The declaration of a datatype: its constructors, or <c>(par (name+) (constructor+))</c>.
    /// </summary>
    private bool DeclaresDatatype(SmtExpression expression)
    {
        if (!expression.IsList || expression.Items.Count == 0)
            return false;

        var first = expression.Items[0];
        if (first.Kind != SmtExpressionKind.Symbol || first.IsQuoted || !string.Equals(first.Text, PARAMETRIC, StringComparison.Ordinal))
            return IsListOf(expression, DeclaresConstructor, mayBeEmpty: false);

        return expression.Items.Count == 3
               && IsListOf(expression.Items[1], IsName, mayBeEmpty: false)
               && IsListOf(expression.Items[2], DeclaresConstructor, mayBeEmpty: false);
    }

    /// <summary>A constructor with its selectors: <c>(name (name sort)*)</c>.</summary>
    private bool DeclaresConstructor(SmtExpression expression)
    {
        if (!expression.IsList || expression.Items.Count == 0 || !Declares(expression.Items[0]))
            return false;

        for (var index = 1; index < expression.Items.Count; index++)
        {
            var selector = expression.Items[index];
            if (!selector.IsList || selector.Items.Count != PAIR || !Declares(selector.Items[0]))
                return false;
        }

        return true;
    }

    private static bool IsListOf(SmtExpression expression, Func<SmtExpression, bool> test, bool mayBeEmpty)
    {
        if (!expression.IsList || (!mayBeEmpty && expression.Items.Count == 0))
            return false;

        foreach (var item in expression.Items)
        {
            if (!test(item))
                return false;
        }

        return true;
    }

    #endregion

    #region Properties

    /// <summary>The names the command last checked declares, in its order; none when it was malformed.</summary>
    public IReadOnlyList<SmtExpression> Names => m_names;

    #endregion
}
