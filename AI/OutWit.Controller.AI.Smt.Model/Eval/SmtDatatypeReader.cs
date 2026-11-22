using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Reads the declarations of algebraic datatypes, in the forms solvers take:
/// <list type="bullet">
/// <item><c>(declare-datatype D ((c (s S)) (d)))</c>, the body optionally <c>(par (T) (...))</c>;</item>
/// <item><c>(declare-datatypes ((D 0) (E 1)) (body-of-D body-of-E))</c>, of SMT-LIB 2.6;</item>
/// <item><c>(declare-datatypes (T) ((D c (d (s T))) (E ...)))</c>, of SMT-LIB 2.5, where a
/// constructor without fields may stand bare.</item>
/// </list>
/// The datatypes of one command may use each other: their names are known before any of their
/// constructors is read.
/// </summary>
internal static class SmtDatatypeReader
{
    #region Constants

    private static readonly string[] NO_PARAMETERS = [];

    #endregion

    #region Functions

    /// <summary>
    /// Reads <c>declare-datatype</c>.
    /// </summary>
    /// <param name="reader">The problem being read.</param>
    /// <param name="name">The datatype's name as written.</param>
    /// <param name="body">Its constructors, or <c>par</c> around them.</param>
    public static void DeclareOne(SmtProblemReader reader, SmtExpression name, SmtExpression body)
    {
        if (!TryBody(body, -1, out var parameters, out var constructors))
        {
            reader.Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        Declare(reader, [(name, parameters, constructors)]);
    }

    /// <summary>
    /// Reads <c>declare-datatypes</c> in either of its forms.
    /// </summary>
    /// <param name="reader">The problem being read.</param>
    /// <param name="first">The items of the command's first list: sort declarations (2.6) or sort parameters (2.5).</param>
    /// <param name="second">The items of its second list: bodies (2.6) or datatypes with their constructors (2.5).</param>
    public static void DeclareTogether(SmtProblemReader reader, IReadOnlyList<SmtExpression> first, IReadOnlyList<SmtExpression> second)
    {
        var group = new List<(SmtExpression Name, string[] Parameters, IReadOnlyList<SmtExpression> Constructors)>();
        var read = first.Count > 0 && first[0].IsList ? TryCurrentForm(first, second, group) : TryOlderForm(first, second, group);
        if (!read)
        {
            reader.Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
            return;
        }

        Declare(reader, group);
    }

    private static bool TryCurrentForm(IReadOnlyList<SmtExpression> declarations, IReadOnlyList<SmtExpression> bodies,
        List<(SmtExpression Name, string[] Parameters, IReadOnlyList<SmtExpression> Constructors)> group)
    {
        if (declarations.Count != bodies.Count)
            return false;

        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (!declaration.IsList || declaration.Items.Count != 2 || !SmtTerms.TryIndex(declaration.Items[1], out var arity))
                return false;

            if (!TryBody(bodies[index], arity, out var parameters, out var constructors))
                return false;

            group.Add((declaration.Items[0], parameters, constructors));
        }

        return true;
    }

    private static bool TryOlderForm(IReadOnlyList<SmtExpression> parameters, IReadOnlyList<SmtExpression> datatypes,
        List<(SmtExpression Name, string[] Parameters, IReadOnlyList<SmtExpression> Constructors)> group)
    {
        if (!SmtProblemReader.TryNames(SmtExpression.List(parameters), out var names))
            return false;

        foreach (var datatype in datatypes)
        {
            if (!datatype.IsList || datatype.Items.Count < 2)
                return false;

            var constructors = new SmtExpression[datatype.Items.Count - 1];
            for (var index = 0; index < constructors.Length; index++)
                constructors[index] = datatype.Items[index + 1];

            group.Add((datatype.Items[0], names, constructors));
        }

        return true;
    }

    /// <summary>
    /// Reads a body: a list of constructors, or <c>(par (T ...) (constructors))</c>.
    /// </summary>
    /// <param name="body">The body as written.</param>
    /// <param name="arity">How many parameters the declaration announced; negative when it announced none.</param>
    /// <param name="parameters">The names of the sort parameters.</param>
    /// <param name="constructors">The constructors as written.</param>
    private static bool TryBody(SmtExpression body, int arity, out string[] parameters, out IReadOnlyList<SmtExpression> constructors)
    {
        parameters = NO_PARAMETERS;
        constructors = [];
        if (!body.IsList)
            return false;

        if (SmtTerms.StartsWithReserved(body, SmtVocabulary.PAR))
        {
            if (body.Items.Count != 3 || !body.Items[2].IsList || !SmtProblemReader.TryNames(body.Items[1], out parameters) || parameters.Length == 0)
                return false;

            constructors = body.Items[2].Items;
        }
        else
        {
            constructors = body.Items;
        }

        return constructors.Count > 0 && (arity < 0 || arity == parameters.Length);
    }

    private static void Declare(SmtProblemReader reader, IReadOnlyList<(SmtExpression Name, string[] Parameters, IReadOnlyList<SmtExpression> Constructors)> group)
    {
        // The names first: the datatypes of one command may use each other.
        var datatypes = new List<SmtDatatype>(group.Count);
        foreach (var (name, parameters, _) in group)
        {
            if (!SmtTerms.IsSymbol(name))
            {
                reader.Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
                return;
            }

            if (reader.Resolver.IsTaken(name.Text))
            {
                reader.Fail(SmtValidationReasons.AMBIGUOUS_SYMBOL);
                return;
            }

            var datatype = new SmtDatatype(name.Text, reader.Resolver.Datatypes, parameters);
            reader.Resolver.Declare(datatype);
            datatypes.Add(datatype);
        }

        for (var index = 0; index < group.Count; index++)
        {
            foreach (var constructor in group[index].Constructors)
            {
                if (!TryConstructor(reader, datatypes[index], constructor))
                {
                    reader.Fail(SmtValidationReasons.ILL_FORMED_COMMAND);
                    return;
                }
            }
        }

        foreach (var datatype in datatypes)
            ResolveFields(reader, datatype);
    }

    private static bool TryConstructor(SmtProblemReader reader, SmtDatatype datatype, SmtExpression constructor)
    {
        var name = constructor.IsList && constructor.Items.Count > 0 ? constructor.Items[0] : constructor;
        if (!SmtTerms.IsSymbol(name))
            return false;

        var count = constructor.IsList ? constructor.Items.Count - 1 : 0;
        var selectors = new string[count];
        var sorts = new SmtExpression[count];
        for (var index = 0; index < count; index++)
        {
            var field = constructor.Items[index + 1];
            if (!field.IsList || field.Items.Count != 2 || !SmtTerms.IsSymbol(field.Items[0]))
                return false;

            selectors[index] = field.Items[0].Text;
            sorts[index] = field.Items[1];
        }

        var made = new SmtConstructor(name.Text, datatype, datatype.Constructors.Count, selectors, sorts);
        datatype.Add(made);

        reader.TryAdd(name.Text, SmtSymbol.OfConstructor(made));
        reader.TryAdd(SmtVocabulary.TESTER_PREFIX + name.Text, SmtSymbol.Tester(made));
        for (var index = 0; index < count; index++)
            reader.TryAdd(selectors[index], SmtSymbol.Selector(made, index));

        return true;
    }

    /// <summary>
    /// A datatype without parameters is one sort, and the sorts of its fields are known now.
    /// One with parameters is a sort for each list of arguments: its fields are read when a
    /// value of such a sort is met.
    /// </summary>
    private static void ResolveFields(SmtProblemReader reader, SmtDatatype datatype)
    {
        if (datatype.Parameters.Count > 0)
            return;

        datatype.Sort = reader.Sorts.Datatype(datatype, []);
        foreach (var constructor in datatype.Constructors)
        {
            var sorts = new SmtSort[constructor.Arity];
            for (var index = 0; index < sorts.Length; index++)
                sorts[index] = reader.Resolver.Resolve(constructor.FieldSortExpressions[index], reader.Sorts, null);

            constructor.FieldSorts = sorts;
        }
    }

    #endregion
}
