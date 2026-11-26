namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// Finds the constants of a problem whose values hold elements of an uninterpreted sort: a sort
/// the problem only declares (<c>declare-sort</c>) and leaves to the solver. The solver names the
/// elements of such a sort itself, inside one model (<c>U!val!0</c>), and takes none of those
/// names in a later command: a value that holds one cannot be said back to it.
/// <para>
/// The values of a sort hold an uninterpreted sort when the sort is one (of any number of
/// arguments), or is made of one where values go:
/// </para>
/// <list type="bullet">
/// <item>a sort of the theories (an array, a sequence, a function sort, and any name that is none
/// of the problem's) holds what each of its arguments holds: a store has an index and an element
/// in it;</item>
/// <item>an alias (<c>define-sort</c>) holds what the sort it stands for holds, its parameters
/// replaced by its arguments;</item>
/// <item>a datatype holds what the fields of its constructors hold, its parameters replaced by
/// its arguments.</item>
/// </list>
/// <para>
/// So an argument counts only where the sort has it in a value: <c>(Opt U)</c> holds <c>U</c>
/// when a constructor of <c>Opt</c> has a field of its parameter, and a datatype or an alias
/// that never uses its parameter holds nothing of its argument.
/// </para>
/// <para>
/// The problem is read as the solver reads it: in the order of its commands, a name being what
/// was declared before its use (a sort that is used before it is declared is the solver's error
/// to report), and a name the problem gave a sort being that sort even where a parameter of an
/// alias or a datatype has the same name. Every sort written in the problem is looked at once,
/// whatever names it: nothing here calls itself on a sort's nesting or on a chain of datatypes,
/// and the datatypes of one declaration that name each other are settled by telling whoever
/// asked, not by reading them again.
/// </para>
/// </summary>
internal sealed class SmtUninterpretedSorts
{
    #region Constants

    private const string PARAMETRIC = "par";
    private const string INDEXED = "_";

    private static readonly IReadOnlyList<SmtExpression> NO_PARAMETERS = [];

    #endregion

    #region Fields

    private readonly HashSet<string> m_uninterpreted = new(StringComparer.Ordinal);

    private readonly Dictionary<string, SmtSortContent> m_made = new(StringComparer.Ordinal);

    private readonly Stack<(SmtSortContent Owner, SmtExpression Sort)> m_sorts = new();

    private readonly Stack<SmtSortContent> m_holders = new();

    #endregion

    #region Constructors

    private SmtUninterpretedSorts()
    {
    }

    #endregion

    #region Functions

    /// <summary>
    /// Finds which of some constants of a problem have values that hold elements of an
    /// uninterpreted sort.
    /// </summary>
    /// <param name="problem">The problem as the sanitizer took it.</param>
    /// <param name="constants">The names of the constants asked about. A name the problem declares more than once is its first declaration.</param>
    /// <returns>For each such constant, by its name: the name of an uninterpreted sort its values hold. Empty when there is none.</returns>
    public static IReadOnlyDictionary<string, string> Held(SmtProblem problem, IReadOnlySet<string> constants)
    {
        var held = new Dictionary<string, string>(StringComparer.Ordinal);

        // Most problems declare no sort of their own: nothing of them has to be read then.
        if (constants.Count == 0 || !problem.SolverCommands.Any(command => command.Head == SmtCommandPolicy.DECLARE_SORT))
            return held;

        var sorts = new SmtUninterpretedSorts();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in problem.SolverCommands)
        {
            var items = command.Items;
            switch (command.Head)
            {
                case SmtCommandPolicy.DECLARE_SORT:
                    sorts.Declare(items[1].Text);
                    break;
                case SmtCommandPolicy.DEFINE_SORT:
                    sorts.Define(items[1].Text, items[2].Items, items[3]);
                    break;
                case SmtCommandPolicy.DECLARE_DATATYPE:
                    sorts.Declare([items[1]], [items[2]]);
                    break;
                case SmtCommandPolicy.DECLARE_DATATYPES:
                    sorts.Declare([.. items[1].Items.Select(named => named.Items[0])], items[2].Items);
                    break;
                case SmtCommandPolicy.DECLARE_CONST when constants.Contains(items[1].Text) && seen.Add(items[1].Text):
                    sorts.Ask(items[1].Text, items[2], held);
                    break;
                case SmtCommandPolicy.DECLARE_FUN when items[2].Items.Count == 0 && constants.Contains(items[1].Text) && seen.Add(items[1].Text):
                    sorts.Ask(items[1].Text, items[3], held);
                    break;
            }
        }

        return held;
    }

    #endregion

    #region Declarations

    /// <summary>A sort the problem leaves to the solver. A name that is taken already stays what it was: the solver refuses the second declaration.</summary>
    private void Declare(string name)
    {
        if (!m_made.ContainsKey(name))
            m_uninterpreted.Add(name);
    }

    /// <summary>An alias: it holds what the sort it stands for holds. It is known by its name only after that sort was read: it cannot name itself.</summary>
    private void Define(string name, IReadOnlyList<SmtExpression> parameters, SmtExpression sort)
    {
        var content = new SmtSortContent(parameters, isOpen: false);
        Read(content, sort);
        Name(name, content);
    }

    /// <summary>
    /// The datatypes of one declaration: each holds what the fields of its constructors hold.
    /// They are known by their names before their fields are read, since a field may be of the
    /// datatype itself or of another one of the declaration.
    /// </summary>
    private void Declare(IReadOnlyList<SmtExpression> names, IReadOnlyList<SmtExpression> declarations)
    {
        var contents = new SmtSortContent[names.Count];
        for (var index = 0; index < contents.Length; index++)
        {
            contents[index] = new SmtSortContent(IsParametric(declarations[index]) ? declarations[index].Items[1].Items : NO_PARAMETERS, isOpen: true);
            Name(names[index].Text, contents[index]);
        }

        for (var index = 0; index < contents.Length; index++)
        {
            var constructors = IsParametric(declarations[index]) ? declarations[index].Items[2].Items : declarations[index].Items;
            foreach (var constructor in constructors)
            {
                // A constructor is its name and its fields, each a selector and its sort.
                for (var field = 1; field < constructor.Items.Count; field++)
                    m_sorts.Push((contents[index], constructor.Items[field].Items[1]));
            }
        }

        Settle();
        foreach (var content in contents)
            content.Close();
    }

    /// <summary>A constant that is asked about: the sort it is declared with is read for it.</summary>
    private void Ask(string name, SmtExpression sort, Dictionary<string, string> held)
    {
        var content = new SmtSortContent(NO_PARAMETERS, isOpen: false);
        Read(content, sort);
        if (content.Uninterpreted != null)
            held[name] = content.Uninterpreted;
    }

    /// <summary>Gives a made sort its name. A name that is taken already stays what it was: the solver refuses the second declaration.</summary>
    private void Name(string name, SmtSortContent content)
    {
        if (!m_uninterpreted.Contains(name))
            m_made.TryAdd(name, content);
    }

    private static bool IsParametric(SmtExpression declaration)
    {
        return declaration.Items[0] is { Kind: SmtExpressionKind.Symbol, IsQuoted: false, Text: PARAMETRIC };
    }

    #endregion

    #region Sorts

    private void Read(SmtSortContent owner, SmtExpression sort)
    {
        m_sorts.Push((owner, sort));
        Settle();
    }

    /// <summary>
    /// Reads every sort that waits to be read, each for the content its values go into, until
    /// none is left: a sort that is read may send its arguments here, and a content that comes
    /// to pass a parameter sends the arguments that were kept for it.
    /// </summary>
    private void Settle()
    {
        while (m_sorts.TryPop(out var next))
        {
            var (owner, sort) = next;
            if (sort.Kind == SmtExpressionKind.Symbol)
                ReadName(owner, sort.Text);
            else if (sort.IsList && sort.Items.Count > 0)
                ReadApplication(owner, sort.Items);
        }
    }

    /// <summary>A sort that is one name: a sort of the problem's, a parameter of the declaration it stands in, or a sort of the theories, which holds nothing of the problem's.</summary>
    private void ReadName(SmtSortContent owner, string name)
    {
        if (m_uninterpreted.Contains(name))
            Hold(owner, name);
        else if (m_made.TryGetValue(name, out var made))
            HoldWhat(owner, made);
        else if (owner.TryParameter(name, out var parameter))
            Pass(owner, parameter);
    }

    /// <summary>A sort with arguments: <c>(name sort+)</c>. An indexed sort (<c>(_ BitVec 8)</c>) has numbers for arguments and holds nothing.</summary>
    private void ReadApplication(SmtSortContent owner, IReadOnlyList<SmtExpression> items)
    {
        var head = items[0];
        if (head is { Kind: SmtExpressionKind.Symbol, IsQuoted: false, Text: INDEXED })
            return;

        // An uninterpreted sort with arguments: its elements are the solver's names whatever the arguments are.
        if (head.Kind == SmtExpressionKind.Symbol && m_uninterpreted.Contains(head.Text))
        {
            Hold(owner, head.Text);
            return;
        }

        if (head.Kind == SmtExpressionKind.Symbol && m_made.TryGetValue(head.Text, out var made))
        {
            HoldWhat(owner, made);

            // An argument is read where the made sort has it in a value; where that is not known
            // yet (a datatype still being declared), the argument is kept until it is.
            for (var argument = 1; argument < items.Count; argument++)
            {
                if (made.Passes(argument - 1))
                    m_sorts.Push((owner, items[argument]));
                else
                    made.Await(argument - 1, owner, items[argument]);
            }

            return;
        }

        // A sort of the theories, or a name the solver will not know either: every argument may be in a value.
        for (var argument = 1; argument < items.Count; argument++)
            m_sorts.Push((owner, items[argument]));
    }

    /// <summary>The values of a content hold the values of a made sort: what that sort holds, they hold, now or when it turns out.</summary>
    private void HoldWhat(SmtSortContent owner, SmtSortContent made)
    {
        if (made.Uninterpreted != null)
            Hold(owner, made.Uninterpreted);
        else
            made.Await(owner);
    }

    /// <summary>Notes an uninterpreted sort in a content's values, and in the values of every content that waited for it, and of those that waited for them.</summary>
    private void Hold(SmtSortContent content, string sort)
    {
        m_holders.Push(content);
        while (m_holders.TryPop(out var holder))
        {
            foreach (var waiting in holder.Hold(sort))
                m_holders.Push(waiting);
        }
    }

    /// <summary>Notes that a content's values hold elements of a parameter's argument: the arguments kept for that parameter are read now.</summary>
    private void Pass(SmtSortContent content, int parameter)
    {
        foreach (var kept in content.Pass(parameter))
            m_sorts.Push(kept);
    }

    #endregion
}
