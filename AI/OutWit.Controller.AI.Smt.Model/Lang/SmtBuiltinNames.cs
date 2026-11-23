using System.Collections.Frozen;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The names the solver has built in and reads in place of a problem's own function of the same
/// name. A problem may declare a name the solver has built in, as the standard allows; for nearly
/// every such name the solver then either reports an error or takes the problem's declaration.
/// For the transcendental functions below it does neither: in a problem whose logic is
/// <c>ALL</c>, or that sets none, it takes the declaration and still reads every application of
/// the name as its own operator. A function of such a name that the problem declares or defines
/// with parameters, and a constructor of such a name with fields, mean one thing to the standard
/// (and to the model evaluator) and another to the solver: such a declaration is refused
/// (<see cref="SmtRefusalCodes.BUILTIN_NAME"/>).
/// <para>
/// A constant of such a name is taken: the solver reads a constant the problem declares as the
/// problem's (a problem may declare <c>pi</c>), and so does every other logic than <c>ALL</c>,
/// where these names are no operators of the solver's. The list is the pinned solver's: its tests
/// ask the solver, name by name and position by position, and hold it to this list.
/// </para>
/// </summary>
internal static class SmtBuiltinNames
{
    #region Constants

    /// <summary>The logic in which the solver has every operator it knows, as it has without a logic.</summary>
    public const string ALL_LOGIC = "ALL";

    /// <summary>The names whose applications the solver reads as its own operator whatever the problem declares.</summary>
    public static readonly FrozenSet<string> OVERRIDING = new[]
    {
        "acos", "acosh", "arctan2", "asin", "asinh", "atan", "atan2", "atanh", "cos", "cosh", "exp", "ln", "log", "sin", "sinh", "tan", "tanh"
    }.ToFrozenSet(StringComparer.Ordinal);

    private const string PARAMETRIC = "par";

    #endregion

    #region Functions

    /// <summary>
    /// Whether the solver reads its own operators over a problem's functions in a logic.
    /// </summary>
    /// <param name="logic">The problem's logic so far; null when it has set none.</param>
    /// <returns>True for no logic and for <c>ALL</c>.</returns>
    public static bool Overrides(string? logic)
    {
        return logic == null || string.Equals(logic, ALL_LOGIC, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first name a well-formed command declares with parameters that the solver reads as
    /// its own operator: a function declared or defined with parameters, a constructor with
    /// fields.
    /// </summary>
    /// <param name="command">A kept command whose shape has been checked.</param>
    /// <param name="head">Its name.</param>
    /// <returns>The name, or null when the command declares none.</returns>
    public static SmtExpression? FirstOverridden(SmtExpression command, string head)
    {
        var items = command.Items;
        switch (head)
        {
            case SmtCommandPolicy.DECLARE_FUN:
            case SmtCommandPolicy.DEFINE_FUN:
            case SmtCommandPolicy.DEFINE_FUN_REC:
                return items[2].Items.Count > 0 && IsOverriding(items[1]) ? items[1] : null;
            case SmtCommandPolicy.DEFINE_FUNS_REC:
                return items[1].Items.FirstOrDefault(declaration => declaration.Items[1].Items.Count > 0 && IsOverriding(declaration.Items[0]))?.Items[0];
            case SmtCommandPolicy.DECLARE_DATATYPE:
                return Constructor(items[2]);
            case SmtCommandPolicy.DECLARE_DATATYPES:
                foreach (var datatype in items[2].Items)
                {
                    if (Constructor(datatype) is { } name)
                        return name;
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>The first constructor with fields of one datatype's declaration that bears such a name.</summary>
    private static SmtExpression? Constructor(SmtExpression datatype)
    {
        var constructors = datatype.Items.Count == 3 && datatype.Items[0].IsSymbol(PARAMETRIC) && !datatype.Items[0].IsQuoted ? datatype.Items[2] : datatype;
        return constructors.Items.FirstOrDefault(constructor => constructor.Items.Count > 1 && IsOverriding(constructor.Items[0]))?.Items[0];
    }

    private static bool IsOverriding(SmtExpression name)
    {
        return name.Kind == SmtExpressionKind.Symbol && OVERRIDING.Contains(name.Text);
    }

    #endregion
}
