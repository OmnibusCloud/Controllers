using System.Numerics;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// Evaluates closed SMT-LIB terms under a model: substitution and evaluation, nothing searched.
/// Core, Ints and Reals (exact rationals), bit-vectors, arrays (finite maps over a default, or
/// functions of the index), datatypes (constructors, selectors, testers, match), uninterpreted
/// sorts (the model's elements), uninterpreted functions (the model's interpretations), the
/// problem's own definitions, and the basic string operators. What it cannot close throws
/// <see cref="SmtPartialException"/> (quantifiers, algebraic numbers, an uninterpreted division
/// by zero, regular expressions, floating point).
/// <para>
/// Independent of the solver by construction: it reads the problem and the printed model only.
/// </para>
/// </summary>
internal sealed class SmtEvaluator
{
    #region Constants

    private const int MAX_CALL_DEPTH = 20_000;

    #endregion

    #region Fields

    private readonly SmtDeclarations m_declarations;
    private readonly SmtModel m_model;
    private readonly Dictionary<string, SmtValue> m_constants = [];
    private int m_depth;

    #endregion

    #region Constructors

    /// <summary>Makes an evaluator for one problem and one model.</summary>
    /// <param name="declarations">The problem's declarations and definitions.</param>
    /// <param name="model">The model.</param>
    public SmtEvaluator(SmtDeclarations declarations, SmtModel model)
    {
        m_declarations = declarations;
        m_model = model;
    }

    #endregion

    #region Functions

    /// <summary>Evaluates a term to a value.</summary>
    /// <param name="term">The term.</param>
    /// <param name="scope">Its local names, or null.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The term cannot be closed.</exception>
    public SmtValue Eval(SExpr term, SmtScope? scope = null)
    {
        switch (term.Kind)
        {
            case SExprKind.Numeral:
                return new SmtNumber(Rational.Of(BigInteger.Parse(term.Text)), false);
            case SExprKind.Decimal:
                return new SmtNumber(Rational.ParseDecimal(term.Text), true);
            case SExprKind.Hexadecimal:
                return new SmtBitVec(BigInteger.Parse("0" + term.Text[2..], System.Globalization.NumberStyles.HexNumber), 4 * (term.Text.Length - 2));
            case SExprKind.Binary:
                return new SmtBitVec(ParseBinary(term.Text[2..]), term.Text.Length - 2);
            case SExprKind.String:
                return SmtString.FromLiteral(term.Text);
            case SExprKind.Symbol:
                return Symbol(term.Text, scope);
            case SExprKind.List:
                return List(term, scope);
            default:
                throw new SmtPartialException("keyword as a term");
        }
    }

    /// <summary>Evaluates a term that must be Boolean.</summary>
    /// <param name="term">The term.</param>
    /// <param name="scope">Its local names, or null.</param>
    /// <returns>Its truth value.</returns>
    /// <exception cref="SmtPartialException">The term cannot be closed, or is not Boolean.</exception>
    public bool Truth(SExpr term, SmtScope? scope = null)
    {
        return Eval(term, scope) is SmtBool b ? b.Value : throw new SmtPartialException("not a Boolean");
    }

    /// <summary>The default value of a sort: what a model completion gives a symbol the model leaves out.</summary>
    /// <param name="sort">The sort.</param>
    /// <param name="depth">Datatype nesting so far.</param>
    /// <returns>The value.</returns>
    public SmtValue DefaultOf(SExpr sort, int depth = 0)
    {
        sort = m_declarations.Expand(sort);
        var name = sort.IsList ? sort.Head : sort.Text;
        switch (name)
        {
            case "Bool":
                return SmtBool.FALSE;
            case "Int":
                return new SmtNumber(Rational.Of(0), false);
            case "Real":
                return new SmtNumber(Rational.Of(0), true);
            case "String":
                return new SmtString([]);
            case "Array" when sort.Items.Count == 3:
                return SmtArray.Constant(DefaultOf(sort.Items[2], depth));
            case "_" when sort.Items.Count == 3 && sort.Items[1].Text == "BitVec":
                return new SmtBitVec(0, int.Parse(sort.Items[2].Text));
        }

        if (name != null && m_declarations.Datatypes.TryGetValue(name, out var constructors) && depth < 16)
        {
            var ordered = constructors.OrderBy(c => m_declarations.Constructors[c].Sorts.Count);
            var first = ordered.First();
            var fields = m_declarations.Constructors[first].Sorts.Select(s => DefaultOf(s, depth + 1)).ToList();
            return new SmtDatatypeValue(first, fields);
        }

        if (name != null && m_declarations.UninterpretedSorts.Contains(name))
            return new SmtUninterpreted(m_model.Universe.TryGetValue(sort.ToString(), out var elements) && elements.Count > 0 ? elements[0] : name + "!default");

        throw new SmtPartialException($"no default for sort {Clip(sort)}");
    }

    private SmtValue Symbol(string name, SmtScope? scope)
    {
        if (scope != null && scope.TryGet(name, out var local))
            return local;

        switch (name)
        {
            case "true":
                return SmtBool.TRUE;
            case "false":
                return SmtBool.FALSE;
        }

        if (m_constants.TryGetValue(name, out var cached))
            return cached;

        SmtValue value;
        if (m_declarations.Definitions.TryGetValue(name, out var definition) && definition.Parameters.Count == 0)
            value = Eval(definition.Body);
        else if (m_model.Values.TryGetValue(name, out var term))
            value = Eval(term);
        else if (m_model.Functions.TryGetValue(name, out var function) && function.Parameters.Count == 0)
            value = Eval(function.Body);
        else if (m_declarations.Constructors.ContainsKey(name))
            value = new SmtDatatypeValue(name, []);
        else if (m_model.Elements.Contains(name))
            value = new SmtUninterpreted(name);
        else if (m_declarations.Symbols.TryGetValue(name, out var declared) && declared.Arguments.Count == 0)
        {
            UsedCompletion = true;
            value = DefaultOf(declared.Result);
        }
        else
            throw new SmtPartialException("a symbol neither declared nor in the model");

        m_constants[name] = value;
        return value;
    }

    private SmtValue List(SExpr term, SmtScope? scope)
    {
        var items = term.Items;
        if (items.Count == 0)
            throw new SmtPartialException("empty application");

        var head = items[0];
        if (head.IsList)
            return Indexed(term, scope);

        switch (head.Text)
        {
            case "_":
                return IndexedConstant(term);
            case "as":
                return Symbol(items[1].Text, scope);
            case "let":
                return Let(term, scope);
            case "!":
                return Eval(items[1], scope);
            case "forall":
            case "exists":
                throw new SmtPartialException("quantifier");
            case "lambda":
                return Lambda(items[1], items[2], scope);
            case "match":
                return SmtMatch.Eval(this, m_declarations, term, scope);
            case "ite":
                return Truth(items[1], scope) ? Eval(items[2], scope) : Eval(items[3], scope);
            case "and":
            case "or":
            case "=>":
                return SmtBool.Of(SmtConnectives.Eval(this, head.Text, items, scope));
            case "root-obj":
                throw new SmtPartialException("algebraic number");
        }

        var arguments = new List<SmtValue>(items.Count - 1);
        for (var i = 1; i < items.Count; i++)
            arguments.Add(Eval(items[i], scope));

        return Apply(head.Text, arguments);
    }

    /// <summary>Applies a named function to values: a constructor, selector or tester, a definition, the model's interpretation, or a built-in operator.</summary>
    /// <param name="name">The function's name.</param>
    /// <param name="arguments">The argument values.</param>
    /// <returns>The value.</returns>
    public SmtValue Apply(string name, List<SmtValue> arguments)
    {
        if (m_declarations.Constructors.ContainsKey(name))
            return new SmtDatatypeValue(name, arguments);

        if (m_declarations.Selectors.TryGetValue(name, out var selector))
        {
            if (arguments[0] is SmtDatatypeValue value && value.Constructor == selector.Constructor)
                return value.Fields[selector.Index];
            if (m_model.Functions.TryGetValue(name, out var interpretation))
                return Call(interpretation, arguments);
            throw new SmtPartialException("selector of another constructor");
        }

        if (name.StartsWith("is-", StringComparison.Ordinal) && m_declarations.Constructors.ContainsKey(name[3..]))
            return SmtBool.Of(arguments[0] is SmtDatatypeValue tested && tested.Constructor == name[3..]);

        if (m_declarations.Definitions.TryGetValue(name, out var definition))
            return Call(definition, arguments);

        if (m_model.Functions.TryGetValue(name, out var function))
            return Call(function, arguments);

        if (m_declarations.Symbols.TryGetValue(name, out var declared))
        {
            UsedCompletion = true;
            return DefaultOf(declared.Result);
        }

        return SmtBuiltins.Apply(name, arguments) ?? SmtBitVecOps.Apply(name, arguments) ?? SmtStringOps.Apply(name, arguments)
               ?? throw new SmtPartialException($"operator {name}");
    }

    private SmtValue Call(SmtDefinition definition, List<SmtValue> arguments)
    {
        if (arguments.Count != definition.Parameters.Count)
            throw new SmtPartialException("arity mismatch");
        if (++m_depth > MAX_CALL_DEPTH)
            throw new SmtPartialException("recursion too deep");

        try
        {
            var names = new Dictionary<string, SmtValue>(definition.Parameters.Count);
            for (var i = 0; i < arguments.Count; i++)
                names[definition.Parameters[i]] = arguments[i];
            return Eval(definition.Body, new SmtScope(names, null));
        }
        finally
        {
            m_depth--;
        }
    }

    private SmtValue Let(SExpr term, SmtScope? scope)
    {
        // Parallel bindings: every value in the enclosing scope, then the body in the new one.
        var bindings = term.Items[1].Items;
        var names = new Dictionary<string, SmtValue>(bindings.Count);
        foreach (var binding in bindings)
            names[binding.Items[0].Text] = Eval(binding.Items[1], scope);
        return Eval(term.Items[2], new SmtScope(names, scope));
    }

    private SmtValue Lambda(SExpr parameters, SExpr body, SmtScope? scope)
    {
        if (parameters.Items.Count != 1)
            throw new SmtPartialException("lambda of several parameters");

        var name = parameters.Items[0].Items[0].Text;
        return SmtArrayModels.FromFunction(this, name, body, scope)
               ?? SmtArray.OfFunction(index => Eval(body, new SmtScope(new Dictionary<string, SmtValue> { [name] = index }, scope)));
    }

    private SmtValue IndexedConstant(SExpr term)
    {
        var items = term.Items;
        var name = items[1].Text;
        if (name.StartsWith("bv", StringComparison.Ordinal) && items.Count == 3)
            return new SmtBitVec(BigInteger.Parse(name[2..]), int.Parse(items[2].Text));

        if (name == "as-array" && items.Count == 3)
        {
            var function = items[2].Text;
            if (!m_model.Functions.TryGetValue(function, out var definition) || definition.Parameters.Count != 1)
                throw new SmtPartialException("as-array of an unknown function");
            return SmtArrayModels.FromFunction(this, definition.Parameters[0], definition.Body, null)
                   ?? SmtArray.OfFunction(index => Call(definition, [index]));
        }

        throw new SmtPartialException($"indexed constant {name}");
    }

    private SmtValue Indexed(SExpr term, SmtScope? scope)
    {
        var head = term.Items[0];
        var arguments = new List<SmtValue>(term.Items.Count - 1);
        for (var i = 1; i < term.Items.Count; i++)
            arguments.Add(Eval(term.Items[i], scope));

        if (head.IsApp("as") && head.Items.Count == 3)
        {
            if (head.Items[1].IsSymbol("const"))
                return SmtArray.Constant(arguments[0]);
            return Apply(head.Items[1].Text, arguments);
        }

        if (head.IsApp("_") && head.Items.Count >= 3)
        {
            var name = head.Items[1].Text;
            if (name == "is")
                return SmtBool.Of(arguments[0] is SmtDatatypeValue tested && tested.Constructor == head.Items[2].Text);

            var indices = head.Items.Skip(2).Select(i => int.Parse(i.Text)).ToList();
            return SmtBitVecOps.ApplyIndexed(name, indices, arguments) ?? throw new SmtPartialException($"indexed operator {name}");
        }

        throw new SmtPartialException("application of a term");
    }

    private static BigInteger ParseBinary(string digits)
    {
        var value = BigInteger.Zero;
        foreach (var digit in digits)
            value = (value << 1) + (digit == '1' ? 1 : 0);
        return value;
    }

    private static string Clip(SExpr sort)
    {
        var text = sort.ToString();
        return text.Length > 40 ? text[..40] : text;
    }

    #endregion

    #region Properties

    /// <summary>Whether a symbol the model left out was given its sort's default value (model completion).</summary>
    public bool UsedCompletion { get; set; }

    #endregion
}
