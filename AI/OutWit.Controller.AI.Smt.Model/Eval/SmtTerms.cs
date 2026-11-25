using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The shapes the evaluator asks an s-expression for before it reads it as a term, a sort or a
/// binder. Nothing here throws: a shape that is not there is an answer, never an exception.
/// </summary>
internal static class SmtTerms
{
    #region Constants

    /// <summary>
    /// How many times the name of a variable is read as text before a binder binds it: when it
    /// is checked to be a name a binder may bind (<see cref="CanBind"/>), and when it is told
    /// apart from the binder's other names (<see cref="AreDistinct"/>). Whoever reads a
    /// binder's names pays for these by each name's length (<see cref="SmtMeter.ChargeName"/>).
    /// </summary>
    public const int BINDER_READS = 2;

    /// <summary>The longest numeral read as an index: no index of the standard needs more digits than an int has.</summary>
    private const int MAX_INDEX_DIGITS = 10;

    #endregion

    #region Functions

    /// <summary>
    /// Whether an s-expression is a reserved word: the symbol of that name, not written between
    /// bars. Between bars the same name is an ordinary symbol.
    /// </summary>
    /// <param name="expression">The s-expression.</param>
    /// <param name="word">A reserved word.</param>
    /// <returns>True for the reserved word.</returns>
    public static bool IsReserved(SmtExpression expression, string word)
    {
        return expression.Kind == SmtExpressionKind.Symbol && !expression.IsQuoted && string.Equals(expression.Text, word, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether an s-expression is a symbol.
    /// </summary>
    /// <param name="expression">The s-expression.</param>
    /// <returns>True for a symbol.</returns>
    public static bool IsSymbol(SmtExpression expression)
    {
        return expression.Kind == SmtExpressionKind.Symbol;
    }

    /// <summary>
    /// Whether an s-expression is a list that starts with a reserved word.
    /// </summary>
    /// <param name="expression">The s-expression.</param>
    /// <param name="word">A reserved word.</param>
    /// <returns>True for such a list.</returns>
    public static bool StartsWithReserved(SmtExpression expression, string word)
    {
        return expression.IsList && expression.Items.Count > 0 && IsReserved(expression.Items[0], word);
    }

    /// <summary>
    /// Reads a numeral that is an index: a width, a position, a count.
    /// </summary>
    /// <param name="expression">The s-expression.</param>
    /// <param name="index">The number.</param>
    /// <returns>True for a numeral an int holds.</returns>
    public static bool TryIndex(SmtExpression expression, out int index)
    {
        index = 0;
        if (expression.Kind != SmtExpressionKind.Numeral || expression.Text.Length == 0 || expression.Text.Length > MAX_INDEX_DIGITS)
            return false;

        long value = 0;
        foreach (var digit in expression.Text)
        {
            if (!char.IsAsciiDigit(digit))
                return false;

            value = value * 10 + (digit - '0');
        }

        if (value > int.MaxValue)
            return false;

        index = (int)value;
        return true;
    }

    /// <summary>
    /// Reads the sorted variables of a binder or of a definition: <c>((x S) (y T))</c>, the
    /// names different from each other and none of them a truth value.
    /// </summary>
    /// <param name="expression">The list of sorted variables.</param>
    /// <param name="names">The names, in order.</param>
    /// <param name="sorts">The sorts as written, in order.</param>
    /// <returns>True for a well-formed list.</returns>
    public static bool TrySortedVariables(SmtExpression expression, out string[] names, out SmtExpression[] sorts)
    {
        names = [];
        sorts = [];
        if (!expression.IsList)
            return false;

        var count = expression.Items.Count;
        var readNames = new string[count];
        var readSorts = new SmtExpression[count];
        for (var index = 0; index < count; index++)
        {
            var variable = expression.Items[index];
            if (!variable.IsList || variable.Items.Count != 2 || !CanBind(variable.Items[0]))
                return false;

            readNames[index] = variable.Items[0].Text;
            readSorts[index] = variable.Items[1];
        }

        if (!AreDistinct(readNames))
            return false;

        names = readNames;
        sorts = readSorts;
        return true;
    }

    /// <summary>
    /// Whether an s-expression can be the name of a local variable: a symbol that is no truth
    /// value and no reserved word. A variable named <c>true</c> would change what every reader
    /// takes <c>true</c> for; a general reserved word is a name only between bars. The name of
    /// a command is an ordinary name, with bars or without.
    /// </summary>
    /// <param name="expression">The s-expression.</param>
    /// <returns>True for a name a binder may bind.</returns>
    public static bool CanBind(SmtExpression expression)
    {
        return expression.Kind == SmtExpressionKind.Symbol && !SmtVocabulary.IsTruthValue(expression.Text)
               && (expression.IsQuoted || !SmtReservedWords.IsGeneral(expression.Text));
    }

    /// <summary>
    /// Whether the names of a list are different from each other.
    /// </summary>
    /// <param name="names">The names.</param>
    /// <returns>True when no name stands twice.</returns>
    public static bool AreDistinct(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
                return false;
        }

        return true;
    }

    #endregion
}
