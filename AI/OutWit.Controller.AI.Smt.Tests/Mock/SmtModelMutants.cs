using System.Globalization;
using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Wrong models made from a right one: each a copy of the solver's answers with the value of
/// one symbol changed to another value of the same sort (a number one further, a truth value
/// negated, a bit-vector's lowest bit flipped, a string one character longer, another element
/// of the universe, another constructor without fields). A mutant is still a model a solver
/// could have printed, so the evaluator has to decide it: it fails, or the changed symbol was
/// free to change.
/// </summary>
internal sealed class SmtModelMutants
{
    #region Constants

    private const string DEFINE_FUN = "define-fun";
    private const string MINUS = "-";
    private const string DIVIDE = "/";
    private const string STORE = "store";
    private const string ITE = "ite";
    private const string LAMBDA = "lambda";
    private const string AS = "as";
    private const string TRUE = "true";
    private const string FALSE = "false";
    private const char DOT = '.';

    #endregion

    #region Fields

    private readonly SmtEvaluationProblem m_problem;
    private readonly IReadOnlyList<SmtExpression> m_model;
    private readonly IReadOnlyList<SmtExpression> m_values;
    private readonly List<(string Sort, string Name)> m_elements = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Reads a fixture: its problem and the solver's answers.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    public SmtModelMutants(SmtFixture fixture)
    {
        m_problem = SmtEvaluationProblemReader.Read(SmtCheck.Commands(fixture.Problem));
        m_model = fixture.Model == null ? [] : SmtReader.Read(fixture.Model, SmtReaderLimits.Unbounded).Single().Items;
        m_values = fixture.Values == null ? [] : SmtReader.Read(fixture.Values, SmtReaderLimits.Unbounded).Single().Items;

        foreach (var element in SmtCheck.Model(fixture.Model, fixture.Values).ElementsInOrder)
            m_elements.Add((element.Items[3].ToString(), element.Items[1].Text));
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the mutants: one for each symbol whose value has a spelling this class can change.
    /// </summary>
    /// <returns>The changed symbol, and the two answers as text (either may be null).</returns>
    public IReadOnlyList<(string Symbol, string? Model, string? Values)> Make()
    {
        var mutants = new List<(string, string?, string?)>();
        foreach (var name in Names())
        {
            // The symbol's value is changed in both answers, the same way: a mutant is what a
            // solver could have printed, and its values say what its model says.
            var changed = false;
            var model = m_model.ToArray();
            for (var index = 0; index < model.Length; index++)
            {
                var item = model[index];
                if (!IsDefinition(item, name) || !TryMutate(item.Items[4], out var body))
                    continue;

                model[index] = SmtExpression.List(item.Items[0], item.Items[1], item.Items[2], item.Items[3], body);
                changed = true;
            }

            var values = m_values.ToArray();
            for (var index = 0; index < values.Length; index++)
            {
                var pair = values[index];
                if (!pair.Items[0].IsSymbol(name) || !TryMutate(pair.Items[1], out var value))
                    continue;

                values[index] = SmtExpression.List(pair.Items[0], value);
                changed = true;
            }

            if (changed)
                mutants.Add((name, model.Length == 0 ? null : SmtExpression.List(model).ToString(), values.Length == 0 ? null : SmtExpression.List(values).ToString()));
        }

        return mutants;
    }

    private IEnumerable<string> Names()
    {
        var defined = m_model.Where(item => item.IsApplication(DEFINE_FUN) && item.Items.Count == 5).Select(item => item.Items[1].Text);
        var valued = m_values.Where(pair => pair.Items[0].Kind == SmtExpressionKind.Symbol).Select(pair => pair.Items[0].Text);
        return defined.Concat(valued).Distinct().OrderBy(name => name, StringComparer.Ordinal);
    }

    private static bool IsDefinition(SmtExpression item, string name)
    {
        return item.IsApplication(DEFINE_FUN) && item.Items.Count == 5 && item.Items[1].IsSymbol(name);
    }

    /// <summary>
    /// Changes a value as the solver spelled it into another value of the same sort.
    /// </summary>
    private bool TryMutate(SmtExpression value, out SmtExpression mutated)
    {
        mutated = value;
        switch (value.Kind)
        {
            case SmtExpressionKind.Numeral:
                mutated = SmtExpression.Atom(SmtExpressionKind.Numeral, (BigInteger.Parse(value.Text, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
                return true;
            case SmtExpressionKind.Decimal:
                var dot = value.Text.IndexOf(DOT);
                var whole = BigInteger.Parse(value.Text[..dot], CultureInfo.InvariantCulture) + 1;
                mutated = SmtExpression.Atom(SmtExpressionKind.Decimal, whole.ToString(CultureInfo.InvariantCulture) + value.Text[dot..]);
                return true;
            case SmtExpressionKind.Hexadecimal:
                var last = Convert.ToInt32(value.Text[^1..], 16) ^ 1;
                mutated = SmtExpression.Atom(SmtExpressionKind.Hexadecimal, value.Text[..^1] + last.ToString("x", CultureInfo.InvariantCulture));
                return true;
            case SmtExpressionKind.Binary:
                mutated = SmtExpression.Atom(SmtExpressionKind.Binary, value.Text[..^1] + (value.Text[^1] == '0' ? '1' : '0'));
                return true;
            case SmtExpressionKind.String:
                mutated = SmtExpression.Atom(SmtExpressionKind.String, value.Text + "a");
                return true;
            case SmtExpressionKind.Symbol:
                return TryMutateSymbol(value, out mutated);
            case SmtExpressionKind.List:
                return TryMutateList(value, out mutated);
            default:
                return false;
        }
    }

    private bool TryMutateSymbol(SmtExpression value, out SmtExpression mutated)
    {
        mutated = value;
        if (value.Text is TRUE or FALSE)
        {
            mutated = SmtExpression.Symbol(value.Text == TRUE ? FALSE : TRUE);
            return true;
        }

        // Another constructor without fields of the same datatype.
        if (m_problem.Symbols.TryGetValue(value.Text, out var symbol) && symbol.Kind == SmtSymbolKind.Constructor)
        {
            var other = symbol.Constructor!.Datatype.Constructors.FirstOrDefault(constructor => constructor.Arity == 0 && constructor.Name != value.Text);
            mutated = other == null ? value : SmtExpression.Symbol(other.Name);
            return other != null;
        }

        // Another element of the same universe.
        var sort = m_elements.FirstOrDefault(element => element.Name == value.Text).Sort;
        var sibling = m_elements.FirstOrDefault(element => element.Sort == sort && element.Name != value.Text).Name;
        mutated = sibling == null ? value : SmtExpression.Symbol(sibling);
        return sibling != null;
    }

    private bool TryMutateList(SmtExpression value, out SmtExpression mutated)
    {
        mutated = value;
        var items = value.Items;
        if (items.Count < 2)
            return false;

        // (_ bvN w): the lowest bit of N.
        if (items[0].IsSymbol("_") && items.Count == 3 && items[1].Text.StartsWith("bv", StringComparison.Ordinal))
        {
            var number = BigInteger.Parse(items[1].Text[2..], CultureInfo.InvariantCulture) ^ BigInteger.One;
            mutated = SmtExpression.List(items[0], SmtExpression.Symbol("bv" + number.ToString(CultureInfo.InvariantCulture)), items[2]);
            return true;
        }

        // Which argument carries the value: the number of (- n) and of (/ n d), the stored
        // value of a store, the value of a constant array, the first result of a function's
        // chain, the body of a lambda, the first field of a constructor.
        var position = items[0].Text switch
        {
            MINUS or DIVIDE => 1,
            STORE when items.Count == 4 => 3,
            ITE when items.Count == 4 => 2,
            LAMBDA when items.Count == 3 => 2,
            _ when items[0].IsApplication(AS) => 1,
            _ when items[0].Kind == SmtExpressionKind.Symbol && m_problem.Symbols.TryGetValue(items[0].Text, out var symbol) && symbol.Kind == SmtSymbolKind.Constructor => 1,
            _ => 0
        };

        if (position == 0 || !TryMutate(items[position], out var changed))
            return false;

        var copy = items.ToArray();
        copy[position] = changed;
        mutated = SmtExpression.List(copy);
        return true;
    }

    #endregion
}
