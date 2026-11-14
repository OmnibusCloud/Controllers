using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// What a problem declares and defines, as the evaluator needs it: sorts and their aliases,
/// datatypes with their constructors, selectors and testers, defined functions (define-fun,
/// define-fun-rec, define-funs-rec), the sorts of declared symbols, and the assertions, with
/// every <c>:named</c> term registered as the 0-ary definition it is.
/// </summary>
internal sealed class SmtDeclarations
{
    #region Constructors

    /// <summary>Reads the declarations of a problem's commands.</summary>
    /// <param name="commands">The canonical problem's commands.</param>
    public SmtDeclarations(IEnumerable<SExpr> commands)
    {
        foreach (var command in commands)
            Read(command);
    }

    #endregion

    #region Functions

    private void Read(SExpr command)
    {
        var items = command.Items;
        switch (command.Head)
        {
            case "declare-sort":
                UninterpretedSorts.Add(items[1].Text);
                break;
            case "define-sort":
                SortAliases[items[1].Text] = (items[2].Items.Select(p => p.Text).ToList(), items[3]);
                break;
            case "declare-const":
                Symbols[items[1].Text] = ([], items[2]);
                break;
            case "declare-fun":
                Symbols[items[1].Text] = (items[2].Items.ToList(), items[3]);
                break;
            case "define-fun":
            case "define-fun-rec":
                Define(items[1].Text, items[2], items[3], items[4]);
                break;
            case "define-funs-rec":
                for (var i = 0; i < items[1].Items.Count; i++)
                {
                    var declaration = items[1].Items[i];
                    Define(declaration.Items[0].Text, declaration.Items[1], declaration.Items[2], items[2].Items[i]);
                }

                break;
            case "declare-datatype":
                ReadDatatype(items[1].Text, items[2]);
                break;
            case "declare-datatypes":
                ReadDatatypes(items);
                break;
            case "assert":
                Assertions.Add(items[1]);
                FindNames(items[1]);
                break;
        }
    }

    private void Define(string name, SExpr parameters, SExpr sort, SExpr body)
    {
        var names = parameters.Items.Select(p => p.Items[0].Text).ToList();
        Definitions[name] = new SmtDefinition(names, parameters.Items.Select(p => p.Items[1]).ToList(), sort, body);
    }

    private void ReadDatatypes(IReadOnlyList<SExpr> items)
    {
        // SMT-LIB 2.6: (declare-datatypes ((D 0) (E 1)) (ctors-of-D (par (T) ctors-of-E)))
        // SMT-LIB 2.5 (Z3 still reads it): (declare-datatypes () ((D ctor ...) (E ctor ...)))
        if (items[1].Items.Count == 0 && items.Count == 3)
        {
            foreach (var datatype in items[2].Items)
                ReadConstructors(datatype.Items[0].Text, datatype.Items.Skip(1).ToList());
            return;
        }

        for (var i = 0; i < items[1].Items.Count; i++)
            ReadDatatype(items[1].Items[i].Items[0].Text, items[2].Items[i]);
    }

    private void ReadDatatype(string name, SExpr body)
    {
        if (body.IsApp("par"))
            body = body.Items[2];
        ReadConstructors(name, body.Items.ToList());
    }

    private void ReadConstructors(string datatype, List<SExpr> constructors)
    {
        var list = new List<string>();
        foreach (var constructor in constructors)
        {
            var name = constructor.IsList ? constructor.Items[0].Text : constructor.Text;
            var fields = constructor.IsList ? constructor.Items.Skip(1).ToList() : [];
            var selectors = fields.Select(f => f.Items[0].Text).ToList();
            var sorts = fields.Select(f => f.Items[1]).ToList();
            Constructors[name] = (datatype, selectors, sorts);
            for (var i = 0; i < selectors.Count; i++)
                Selectors[selectors[i]] = (name, i);
            list.Add(name);
        }

        Datatypes[datatype] = list;
    }

    private void FindNames(SExpr term)
    {
        var stack = new Stack<SExpr>();
        stack.Push(term);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!current.IsList)
                continue;

            if (current.IsApp("!"))
            {
                for (var i = 2; i + 1 < current.Items.Count; i += 2)
                {
                    if (current.Items[i].Text == ":named")
                        Definitions[current.Items[i + 1].Text] = new SmtDefinition([], [], null, current.Items[1]);
                }
            }

            foreach (var item in current.Items)
                stack.Push(item);
        }
    }

    /// <summary>Expands a sort alias of define-sort, once per level, until none is left.</summary>
    /// <param name="sort">A sort expression.</param>
    /// <returns>The sort with every alias expanded.</returns>
    public SExpr Expand(SExpr sort)
    {
        for (var depth = 0; depth < 64; depth++)
        {
            var name = sort.IsList ? sort.Head : sort.Text;
            if (name == null || !SortAliases.TryGetValue(name, out var alias))
                return sort;

            var arguments = sort.IsList ? sort.Items.Skip(1).ToList() : [];
            sort = Substitute(alias.Body, alias.Parameters, arguments);
        }

        throw new SmtPartialException("sort aliases nested too deep");
    }

    private static SExpr Substitute(SExpr sort, List<string> parameters, List<SExpr> arguments)
    {
        if (!sort.IsList)
        {
            var at = parameters.IndexOf(sort.Text);
            return at >= 0 && at < arguments.Count ? arguments[at] : sort;
        }

        return SExpr.List(sort.Items.Select(i => Substitute(i, parameters, arguments)).ToList());
    }

    #endregion

    #region Properties

    /// <summary>The sorts of declare-sort.</summary>
    public HashSet<string> UninterpretedSorts { get; } = [];

    /// <summary>The aliases of define-sort: parameters and body.</summary>
    public Dictionary<string, (List<string> Parameters, SExpr Body)> SortAliases { get; } = [];

    /// <summary>Declared symbols (declare-const, declare-fun): argument sorts and result sort.</summary>
    public Dictionary<string, (List<SExpr> Arguments, SExpr Result)> Symbols { get; } = [];

    /// <summary>Defined functions (define-fun, define-fun-rec, define-funs-rec) and :named terms.</summary>
    public Dictionary<string, SmtDefinition> Definitions { get; } = [];

    /// <summary>Datatypes and their constructors, in declaration order.</summary>
    public Dictionary<string, List<string>> Datatypes { get; } = [];

    /// <summary>Constructors: their datatype, selectors and field sorts.</summary>
    public Dictionary<string, (string Datatype, List<string> Selectors, List<SExpr> Sorts)> Constructors { get; } = [];

    /// <summary>Selectors: their constructor and field index.</summary>
    public Dictionary<string, (string Constructor, int Index)> Selectors { get; } = [];

    /// <summary>The assertions, in order.</summary>
    public List<SExpr> Assertions { get; } = [];

    #endregion
}
