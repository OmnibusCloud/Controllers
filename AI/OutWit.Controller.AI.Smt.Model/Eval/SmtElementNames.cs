namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The names a solver gives the elements of an uninterpreted sort: the sort's name, the mark
/// <c>!val!</c> and a number (<c>A!val!0</c> is an element of <c>A</c>). A model may name such
/// an element without listing the universe of its sort; the closed script declares it and the
/// evaluator reads it by this one rule.
/// </summary>
internal static class SmtElementNames
{
    #region Constants

    private const string MARK = "!val!";

    #endregion

    #region Functions

    /// <summary>
    /// Splits the name of an element into the name of its sort: everything before the last
    /// mark, which digits, and nothing else, follow.
    /// </summary>
    /// <param name="name">A symbol's name.</param>
    /// <param name="sort">The name of the sort the element would be of; empty when the name is no element's.</param>
    /// <returns>True when the name is written as an element's.</returns>
    public static bool TrySortOf(string name, out string sort)
    {
        sort = "";
        var mark = name.LastIndexOf(MARK, StringComparison.Ordinal);
        var digits = mark + MARK.Length;
        if (mark <= 0 || digits == name.Length)
            return false;

        for (var index = digits; index < name.Length; index++)
        {
            if (!char.IsAsciiDigit(name[index]))
                return false;
        }

        sort = name[..mark];
        return true;
    }

    #endregion
}
