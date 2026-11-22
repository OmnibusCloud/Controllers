using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The definitions a closed script takes from a model: one for each declared symbol the model
/// gives a value or an interpretation, in the sorts the problem declares, and the functions the
/// model added for itself, as it printed them. They are put in an order in which each stands
/// after the ones it mentions, since a solver reads a definition with what is defined so far.
/// </summary>
internal sealed class SmtClosedDefinitions
{
    #region Constants

    private const string DECLARE_CONST = "declare-const";
    private const string DEFINE_FUN = "define-fun";
    private const byte OPEN = 1;
    private const byte DONE = 2;

    #endregion

    #region Fields

    private readonly SmtModel m_model;
    private readonly Dictionary<string, SmtExpression> m_definitions = new(StringComparer.Ordinal);
    private readonly List<string> m_names = [];
    private readonly HashSet<string> m_taken = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>
    /// Starts collecting the definitions of one script.
    /// </summary>
    /// <param name="model">The model.</param>
    public SmtClosedDefinitions(SmtModel model)
    {
        m_model = model;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Records a name the problem gives a meaning itself: the model defines no such name.
    /// </summary>
    /// <param name="name">The name.</param>
    public void Reserve(string name)
    {
        m_taken.Add(name);
    }

    /// <summary>
    /// Turns the declaration of a constant or a function into its definition by the model.
    /// </summary>
    /// <param name="declaration">A <c>declare-const</c> or a <c>declare-fun</c>, well-formed.</param>
    /// <returns>False when the model leaves the symbol out, or defines it with another number of arguments, or the problem declares the name twice: the declaration then stays.</returns>
    public bool TryDefine(SmtExpression declaration)
    {
        var items = declaration.Items;
        var name = items[1];
        var isConstant = declaration.Head == DECLARE_CONST;
        IReadOnlyList<SmtExpression> parameterSorts = isConstant ? [] : items[2].Items;
        var sort = isConstant ? items[2] : items[3];
        if (!m_taken.Add(name.Text))
            return false;

        SmtExpression parameters;
        SmtExpression body;
        if (parameterSorts.Count == 0)
        {
            if (!m_model.TryGetConstant(name.Text, out body))
                return false;

            parameters = SmtExpression.List();
        }
        else
        {
            if (!m_model.Functions.TryGetValue(name.Text, out var function) || function.Parameters.Count != parameterSorts.Count)
                return false;

            // The model's names for the parameters, the problem's sorts for them.
            var variables = new SmtExpression[parameterSorts.Count];
            for (var index = 0; index < variables.Length; index++)
                variables[index] = SmtExpression.List(function.Definition.Items[2].Items[index].Items[0], parameterSorts[index]);

            parameters = SmtExpression.List(variables);
            body = function.Body;
        }

        Add(name.Text, SmtExpression.List(SmtExpression.Symbol(DEFINE_FUN), name, parameters, sort, body));
        Declared++;
        return true;
    }

    /// <summary>
    /// The definitions, each after the ones it mentions; the functions the model added for
    /// itself are among them.
    /// </summary>
    /// <returns>The definitions, in an order a solver can read them in.</returns>
    public IReadOnlyList<SmtExpression> InOrderOfUse()
    {
        foreach (var function in m_model.FunctionsInOrder)
        {
            if (m_taken.Add(function.Name))
                Add(function.Name, function.Definition);
        }

        var ordered = new List<SmtExpression>(m_names.Count);
        var states = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var name in m_names)
            Emit(name, states, ordered);

        return ordered;
    }

    private void Add(string name, SmtExpression definition)
    {
        m_definitions[name] = definition;
        m_names.Add(name);
    }

    /// <summary>
    /// Puts a definition out after everything it mentions, with a stack of its own: a chain of
    /// definitions may be longer than calls should nest. Definitions that mention each other in
    /// a circle are no model a solver prints; they come out in the order they are met.
    /// </summary>
    private void Emit(string root, Dictionary<string, byte> states, List<SmtExpression> ordered)
    {
        if (states.ContainsKey(root))
            return;

        var pending = new Stack<(string Name, List<string> Uses, int Next)>();
        states[root] = OPEN;
        pending.Push((root, Uses(root), 0));
        while (pending.Count > 0)
        {
            var (name, uses, next) = pending.Pop();
            if (next >= uses.Count)
            {
                states[name] = DONE;
                ordered.Add(m_definitions[name]);
                continue;
            }

            pending.Push((name, uses, next + 1));
            var used = uses[next];
            if (states.ContainsKey(used))
                continue;

            states[used] = OPEN;
            pending.Push((used, Uses(used), 0));
        }
    }

    /// <summary>
    /// The other definitions a definition's body mentions, in the order it mentions them.
    /// </summary>
    private List<string> Uses(string name)
    {
        var uses = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { name };
        var pending = new Stack<SmtExpression>();
        pending.Push(m_definitions[name].Items[4]);
        while (pending.Count > 0)
        {
            var term = pending.Pop();
            if (term.Kind == SmtExpressionKind.Symbol && m_definitions.ContainsKey(term.Text) && seen.Add(term.Text))
                uses.Add(term.Text);

            for (var index = term.Items.Count - 1; index >= 0; index--)
                pending.Push(term.Items[index]);
        }

        return uses;
    }

    #endregion

    #region Properties

    /// <summary>How many declared symbols were turned into definitions.</summary>
    public int Declared { get; private set; }

    #endregion
}
