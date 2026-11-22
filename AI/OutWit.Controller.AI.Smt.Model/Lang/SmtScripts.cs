using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The commands the controller itself puts around a problem: what asks the solver to act, the
/// options, the assertion a query adds. Each is built as an s-expression and written by the
/// writer that writes a problem, never by formatting text: a term that goes into a command
/// goes in as the s-expression it is, whatever it holds.
/// </summary>
public static class SmtScripts
{
    #region Constants

    /// <summary>
    /// The greatest resource limit the solver takes: its parameter is 32 bits wide. Given a
    /// greater one it reports an error and then runs the check with no limit at all.
    /// </summary>
    public const ulong MAX_RESOURCE_LIMIT = uint.MaxValue;

    private const string CHECK_SAT = "check-sat";
    private const string GET_MODEL = "get-model";
    private const string GET_VALUE = "get-value";
    private const string GET_INFO = "get-info";
    private const string SET_OPTION = "set-option";
    private const string ASSERT = "assert";
    private const string NOT = "not";
    private const string AND = "and";
    private const string EQUALS = "=";
    private const string RESOURCE_LIMIT = ":rlimit";
    private const char COLON = ':';
    private const char LINE_FEED = '\n';

    #endregion

    #region Functions

    /// <summary>
    /// The command that asks for the verdict.
    /// </summary>
    /// <returns><c>(check-sat)</c>.</returns>
    public static SmtExpression CheckSat()
    {
        return SmtExpression.List(SmtExpression.Symbol(CHECK_SAT));
    }

    /// <summary>
    /// The command that asks for the model of a satisfiable problem.
    /// </summary>
    /// <returns><c>(get-model)</c>.</returns>
    public static SmtExpression GetModel()
    {
        return SmtExpression.List(SmtExpression.Symbol(GET_MODEL));
    }

    /// <summary>
    /// The command that asks for the values of terms in the model.
    /// </summary>
    /// <param name="terms">The terms, one or more.</param>
    /// <returns><c>(get-value (t1 ... tn))</c>.</returns>
    /// <exception cref="ArgumentException">There is no term.</exception>
    public static SmtExpression GetValue(IReadOnlyList<SmtExpression> terms)
    {
        var asked = Copy(terms, nameof(terms));
        if (asked.Length == 0)
            throw new ArgumentException("get-value asks for one term or more", nameof(terms));

        return SmtExpression.List(SmtExpression.Symbol(GET_VALUE), SmtExpression.List(asked));
    }

    /// <summary>
    /// The command that asks the solver for a piece of information about itself or its last check.
    /// </summary>
    /// <param name="keyword">The keyword of the information: a colon and a simple symbol (<c>:reason-unknown</c>).</param>
    /// <returns><c>(get-info keyword)</c>.</returns>
    /// <exception cref="ArgumentException">The text is no keyword.</exception>
    public static SmtExpression GetInfo(string keyword)
    {
        return SmtExpression.List(SmtExpression.Symbol(GET_INFO), Keyword(keyword, nameof(keyword)));
    }

    /// <summary>
    /// The command that sets an option of the solver.
    /// </summary>
    /// <param name="keyword">The keyword of the option: a colon and a simple symbol (<c>:produce-models</c>).</param>
    /// <param name="value">The value.</param>
    /// <returns><c>(set-option keyword value)</c>.</returns>
    /// <exception cref="ArgumentException">The text is no keyword.</exception>
    public static SmtExpression SetOption(string keyword, SmtExpression value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return SmtExpression.List(SmtExpression.Symbol(SET_OPTION), Keyword(keyword, nameof(keyword)), value);
    }

    /// <summary>
    /// The command that sets the resource limit of the checks that follow.
    /// </summary>
    /// <param name="rlimit">The limit, at most <see cref="MAX_RESOURCE_LIMIT"/>.</param>
    /// <returns><c>(set-option :rlimit N)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The limit is above what the solver takes.</exception>
    public static SmtExpression SetResourceLimit(ulong rlimit)
    {
        if (rlimit > MAX_RESOURCE_LIMIT)
            throw new ArgumentOutOfRangeException(nameof(rlimit), rlimit, $"the solver's resource limit is at most {MAX_RESOURCE_LIMIT}");

        var value = SmtExpression.Atom(SmtExpressionKind.Numeral, rlimit.ToString(CultureInfo.InvariantCulture));
        return SetOption(RESOURCE_LIMIT, value);
    }

    /// <summary>
    /// The assertion that turns a goal into a question of satisfiability: the goal follows from
    /// the problem exactly when the problem with this assertion is unsatisfiable.
    /// </summary>
    /// <param name="goal">The goal: a Boolean term.</param>
    /// <returns><c>(assert (not goal))</c>.</returns>
    public static SmtExpression AssertNot(SmtExpression goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        return Assert(Not(goal));
    }

    /// <summary>
    /// The assertion that rules a found model out, so that the next check finds another one or
    /// none: not every term has the value it had.
    /// </summary>
    /// <param name="terms">The terms the models are told apart by, one or more.</param>
    /// <param name="values">The value of each term in the model found, in the same order.</param>
    /// <returns><c>(assert (not (and (= t1 v1) ... (= tn vn))))</c>; with one term, <c>(assert (not (= t1 v1)))</c>.</returns>
    /// <exception cref="ArgumentException">There is no term, or the terms and the values differ in number.</exception>
    public static SmtExpression Block(IReadOnlyList<SmtExpression> terms, IReadOnlyList<SmtExpression> values)
    {
        var left = Copy(terms, nameof(terms));
        var right = Copy(values, nameof(values));
        if (left.Length == 0)
            throw new ArgumentException("a model is ruled out by the value of one term or more", nameof(terms));

        if (left.Length != right.Length)
            throw new ArgumentException($"{left.Length} terms and {right.Length} values: each term has one value", nameof(values));

        if (left.Length == 1)
            return Assert(Not(Equal(left[0], right[0])));

        var conjunction = new SmtExpression[left.Length + 1];
        conjunction[0] = SmtExpression.Symbol(AND);
        for (var index = 0; index < left.Length; index++)
            conjunction[index + 1] = Equal(left[index], right[index]);

        return Assert(Not(SmtExpression.List(conjunction)));
    }

    /// <summary>
    /// The text of a sequence of commands: one command a line as <see cref="SmtExpression.WriteTo"/>
    /// writes it, every line ended by a line feed.
    /// </summary>
    /// <param name="commands">The commands, in order.</param>
    /// <returns>The text; empty for no command.</returns>
    public static string Text(IEnumerable<SmtExpression> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var builder = new StringBuilder();
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command, nameof(commands));
            command.WriteTo(builder);
            builder.Append(LINE_FEED);
        }

        return builder.ToString();
    }

    #endregion

    #region Tools

    private static SmtExpression Assert(SmtExpression term)
    {
        return SmtExpression.List(SmtExpression.Symbol(ASSERT), term);
    }

    private static SmtExpression Not(SmtExpression term)
    {
        return SmtExpression.List(SmtExpression.Symbol(NOT), term);
    }

    private static SmtExpression Equal(SmtExpression left, SmtExpression right)
    {
        return SmtExpression.List(SmtExpression.Symbol(EQUALS), left, right);
    }

    private static SmtExpression Keyword(string keyword, string parameter)
    {
        ArgumentNullException.ThrowIfNull(keyword, parameter);
        if (keyword.Length < 2 || keyword[0] != COLON || !SmtLexicon.IsSimpleSymbol(keyword.AsSpan(1)))
            throw new ArgumentException("a keyword is a colon and a simple symbol", parameter);

        return SmtExpression.Atom(SmtExpressionKind.Keyword, keyword);
    }

    /// <summary>The caller's terms as a list of the command's own: what the caller does to its list afterwards does not reach the command.</summary>
    private static SmtExpression[] Copy(IReadOnlyList<SmtExpression> expressions, string parameter)
    {
        ArgumentNullException.ThrowIfNull(expressions, parameter);

        var copy = new SmtExpression[expressions.Count];
        for (var index = 0; index < copy.Length; index++)
            copy[index] = expressions[index] ?? throw new ArgumentException("a term is missing", parameter);

        return copy;
    }

    #endregion
}
