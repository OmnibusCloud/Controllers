namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// Takes the top-level forms of a text one by one and makes a problem of them, or refuses the
/// first one that breaks a rule. For each form, in this order: it is a command; its name is
/// written without bars; it is kept, stripped or refused by its name; a kept command does not
/// stand after the problem's end; its atoms pass the token rules, in the order of the text; its
/// shape is the standard's; the names it declares are the problem's own and not too many.
/// <para>
/// A problem ends at its first <c>check-sat</c> and at its <c>exit</c>. After <c>exit</c> a
/// solver runs nothing, so a command there is part of the text and not of what a solver given
/// the text would solve. A declaration or an assertion after a <c>check-sat</c> makes the script
/// an incremental one: its first answer is for less than the whole text. Questions to the
/// solver may follow either: they are stripped wherever they stand.
/// </para>
/// </summary>
internal sealed class SmtProblemBuilder
{
    #region Constants

    private const string STATUS = ":status";
    private const string SOURCE = ":source";
    private const string LICENSE = ":license";
    private const string SAT = "sat";
    private const string UNSAT = "unsat";

    private const string REASON_AFTER_EXIT = "the command stands after exit: a solver runs nothing after it";
    private const string REASON_AFTER_CHECK = "the command stands after check-sat: a script that goes on after a check is incremental, and its first answer is for less than the whole text";

    #endregion

    #region Fields

    private readonly List<SmtExpression> m_commands = [];
    private readonly List<SmtExpression> m_solverCommands = [];
    private readonly List<SmtDeclaredConstant> m_constants = [];
    private readonly List<SmtDeclaredFunction> m_functions = [];
    private readonly Dictionary<string, int> m_stripped = new(StringComparer.Ordinal);
    private readonly List<SmtStrippedCommand> m_strippedLines = [];
    private readonly SmtCommandShapes m_shapes = new();
    private string? m_logic;
    private SmtProblemStatus m_status;
    private string m_source = "";
    private string m_license = "";
    private int m_assertions;
    private int m_declared;
    private bool m_checked;
    private bool m_exited;

    #endregion

    #region Functions

    /// <summary>
    /// Takes the next top-level form of the text.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>Null when the form was kept or stripped; the refusal of the problem otherwise.</returns>
    public SmtRefusalData? Take(SmtExpression form)
    {
        var head = form.Head;
        if (head == null)
            return SmtRefusals.Of(SmtRefusalCodes.NOT_A_COMMAND, form.Line, "", WhyNoCommand(form));

        if (form.Items[0].IsQuoted)
            return SmtRefusals.Of(SmtRefusalCodes.COMMAND_NOT_ALLOWED, form.Line, head, "a command's name is written without bars");

        if (SmtCommandPolicy.IsStripped(head))
        {
            m_stripped[head] = m_stripped.GetValueOrDefault(head) + 1;
            m_strippedLines.Add(new SmtStrippedCommand(form.Line, head));
            m_checked |= string.Equals(head, SmtCommandPolicy.CHECK_SAT, StringComparison.Ordinal);
            m_exited |= string.Equals(head, SmtCommandPolicy.EXIT, StringComparison.Ordinal);
            return null;
        }

        if (!SmtCommandPolicy.IsKept(head))
            return SmtRefusals.Of(SmtRefusalCodes.COMMAND_NOT_ALLOWED, form.Line, head, SmtCommandPolicy.WhyRefused(head));

        if (m_exited || m_checked)
            return SmtRefusals.Of(SmtRefusalCodes.COMMAND_AFTER_END, form.Line, head, m_exited ? REASON_AFTER_EXIT : REASON_AFTER_CHECK);

        var refusal = SmtTokenRules.Check(form, head) ?? CheckShape(form, head) ?? CheckNames(form, head);
        if (refusal == null)
            Keep(form, head);

        return refusal;
    }

    /// <summary>
    /// Makes the problem of the forms taken.
    /// </summary>
    /// <returns>The problem.</returns>
    public SmtProblem Build()
    {
        return new SmtProblem(m_commands, m_solverCommands, m_logic, m_status, m_source, m_license, m_constants, m_functions, m_assertions, m_declared, m_stripped, m_strippedLines);
    }

    private SmtRefusalData? CheckShape(SmtExpression form, string head)
    {
        var fault = m_shapes.Check(form, head);
        return fault == null ? null : SmtRefusals.Of(SmtRefusalCodes.COMMAND_MALFORMED, form.Line, head, fault);
    }

    private SmtRefusalData? CheckNames(SmtExpression form, string head)
    {
        foreach (var name in m_shapes.Names)
        {
            if (name.Text.StartsWith(SmtSanitizer.RESERVED_PREFIX, StringComparison.Ordinal))
            {
                return SmtRefusals.Of(SmtRefusalCodes.RESERVED_PREFIX, name.Line, head,
                    $"the name '{SmtRefusals.Token(name.Text)}' starts with '{SmtSanitizer.RESERVED_PREFIX}', the prefix of the controller's own names");
            }
        }

        // Counted as a long: a problem's commands cannot overflow it, whatever the limits become.
        if ((long)m_declared + m_shapes.Names.Count > SmtSanitizer.MAX_DECLARED_SYMBOLS)
            return SmtRefusals.Of(SmtRefusalCodes.TOO_MANY_SYMBOLS, form.Line, head, $"the problem declares more than the {SmtSanitizer.MAX_DECLARED_SYMBOLS} names allowed");

        m_declared += m_shapes.Names.Count;
        return null;
    }

    private void Keep(SmtExpression form, string head)
    {
        // A piece of information is kept with its line breaks written one way: the solver never
        // gets it, and the problem is the same problem whichever way its file ends a line.
        if (string.Equals(head, SmtCommandPolicy.SET_INFO, StringComparison.Ordinal))
            form = SmtLineBreaks.Normalize(form);

        var items = form.Items;
        switch (head)
        {
            case SmtCommandPolicy.SET_LOGIC:
                m_logic ??= items[1].Text;
                break;
            case SmtCommandPolicy.SET_INFO:
                ReadInformation(items);
                break;
            case SmtCommandPolicy.DECLARE_CONST:
                m_constants.Add(new SmtDeclaredConstant(items[1], items[2]));
                break;
            case SmtCommandPolicy.DECLARE_FUN when items[2].Items.Count == 0:
                m_constants.Add(new SmtDeclaredConstant(items[1], items[3]));
                break;
            case SmtCommandPolicy.DECLARE_FUN:
                m_functions.Add(new SmtDeclaredFunction(items[1], items[2].Items, items[3]));
                break;
            case SmtCommandPolicy.ASSERT:
                m_assertions++;
                break;
        }

        m_commands.Add(form);
        if (SmtCommandPolicy.IsForSolver(head))
            m_solverCommands.Add(form);
    }

    /// <summary>
    /// The pieces of information the controller reads itself. The last one of a kind holds; one
    /// whose value is not of the expected kind reads as none.
    /// </summary>
    private void ReadInformation(IReadOnlyList<SmtExpression> items)
    {
        var value = items.Count > 2 ? items[2] : null;
        switch (items[1].Text)
        {
            case STATUS:
                m_status = value is { Kind: SmtExpressionKind.Symbol } ? StatusOf(value.Text) : SmtProblemStatus.None;
                break;
            case SOURCE:
                m_source = Attribution(value);
                break;
            case LICENSE:
                m_license = Attribution(value);
                break;
        }
    }

    private static SmtProblemStatus StatusOf(string verdict)
    {
        return verdict switch
        {
            SAT => SmtProblemStatus.Sat,
            UNSAT => SmtProblemStatus.Unsat,
            _ => SmtProblemStatus.None
        };
    }

    /// <summary>A string's content or a symbol's name, cut to what an attribution list takes; whole surrogate pairs only.</summary>
    private static string Attribution(SmtExpression? value)
    {
        if (value is not { Kind: SmtExpressionKind.String or SmtExpressionKind.Symbol })
            return "";

        var text = value.Text;
        if (text.Length <= SmtSanitizer.ATTRIBUTION_CHARACTERS)
            return text;

        var kept = SmtSanitizer.ATTRIBUTION_CHARACTERS;
        if (char.IsHighSurrogate(text[kept - 1]))
            kept--;

        return text[..kept];
    }

    private static string WhyNoCommand(SmtExpression form)
    {
        if (!form.IsList)
            return "a command is a list that starts with its name; this is an atom";

        return form.Items.Count == 0
            ? "a command is a list that starts with its name; this list is empty"
            : "a command is a list that starts with its name; this list starts with something else";
    }

    #endregion
}
