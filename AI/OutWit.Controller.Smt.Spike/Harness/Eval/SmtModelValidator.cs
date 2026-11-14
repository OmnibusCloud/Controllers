using System.Diagnostics;
using System.Numerics;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// The host-side check of a sat answer (decision 6 of 2026-11-14): every assertion of the problem
/// evaluated under the model the solver printed. <c>validated</c> when all are true, <c>failed</c>
/// when one is false (and no completed symbol was needed for it), <c>partial</c> otherwise.
/// Also makes wrong models on purpose (one constant's value changed) to show that a wrong model
/// is caught.
/// </summary>
internal sealed class SmtModelValidator
{
    #region Fields

    private readonly SmtDeclarations m_declarations;
    private readonly HashSet<string> m_mentioned;

    #endregion

    #region Constructors

    /// <summary>Reads a problem's declarations once, for any number of models.</summary>
    /// <param name="problem">The canonical problem.</param>
    public SmtModelValidator(SmtProblem problem)
    {
        m_declarations = new SmtDeclarations(problem.Commands);
        m_mentioned = Symbols(m_declarations.Assertions);
    }

    #endregion

    #region Functions

    /// <summary>Checks one model.</summary>
    /// <param name="model">The model (get-model and get-value).</param>
    /// <returns>The validation class and counts.</returns>
    public SmtValidation Check(SmtModel model)
    {
        var clock = Stopwatch.StartNew();
        var evaluator = new SmtEvaluator(m_declarations, model);
        var trueCount = 0;
        string? partial = null;
        string? failed = null;

        for (var i = 0; i < m_declarations.Assertions.Count; i++)
        {
            evaluator.UsedCompletion = false;
            try
            {
                if (evaluator.Truth(m_declarations.Assertions[i]))
                    trueCount++;
                else if (evaluator.UsedCompletion)
                    partial ??= "false only with a completed symbol";
                else
                    failed ??= $"assertion {i + 1} is false";
            }
            catch (SmtPartialException exception)
            {
                partial ??= exception.Reason;
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException or InvalidCastException or IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException or NullReferenceException)
            {
                partial ??= "unreadable term (" + exception.GetType().Name + ")";
            }
        }

        var cls = failed != null ? "failed" : partial != null ? "partial" : "validated";
        return new SmtValidation
        {
            Class = cls,
            Reason = failed ?? partial,
            Assertions = m_declarations.Assertions.Count,
            True = trueCount,
            Defined = model.Values.Count + model.Functions.Count(f => f.Value.Parameters.Count > 0),
            UsedCompletion = evaluator.UsedCompletion,
            Microseconds = clock.Elapsed.Ticks / 10
        };
    }

    /// <summary>
    /// Wrong models made from a right one: up to <paramref name="count"/> copies, each with one
    /// constant that an assertion mentions given another value (an Int or Real plus one, a Bool
    /// negated, a bit-vector's lowest bit flipped, another constructor, a string with one more
    /// character).
    /// </summary>
    /// <param name="model">The model the solver printed.</param>
    /// <param name="count">How many mutants at most.</param>
    /// <returns>(constant, mutated model) pairs.</returns>
    public List<(string Constant, SmtModel Model)> Mutants(SmtModel model, int count)
    {
        var mutants = new List<(string, SmtModel)>();
        var evaluator = new SmtEvaluator(m_declarations, model);
        foreach (var name in model.Values.Keys.Where(m_mentioned.Contains).OrderBy(n => n, StringComparer.Ordinal))
        {
            if (mutants.Count >= count)
                break;

            SExpr? changed;
            try
            {
                changed = Mutate(evaluator.Eval(model.Values[name]));
            }
            catch (SmtPartialException)
            {
                continue;
            }

            if (changed == null)
                continue;

            var copy = new SmtModel();
            foreach (var (key, value) in model.Values)
                copy.Values[key] = key == name ? changed : value;
            foreach (var (key, value) in model.Functions)
                copy.Functions[key] = value;
            foreach (var (key, value) in model.Universe)
                copy.Universe[key] = value;
            copy.Elements.UnionWith(model.Elements);
            mutants.Add((name, copy));
        }

        return mutants;
    }

    private SExpr? Mutate(SmtValue value)
    {
        switch (value)
        {
            case SmtBool b:
                return SExpr.Sym(b.Value ? "false" : "true");
            case SmtNumber n:
                return Term(n.Value + Rational.Of(1), n.IsReal);
            case SmtBitVec v:
                return SExpr.List([SExpr.Sym("_"), SExpr.Sym("bv" + (v.Value ^ BigInteger.One)), SExpr.Atom(SExprKind.Numeral, v.Width.ToString())]);
            case SmtString s:
                return SExpr.Atom(SExprKind.String, new string(s.CodePoints.Select(c => (char)c).ToArray()) + "a");
            case SmtDatatypeValue d:
                var datatype = m_declarations.Constructors[d.Constructor].Datatype;
                var other = m_declarations.Datatypes[datatype].FirstOrDefault(c => c != d.Constructor && m_declarations.Constructors[c].Sorts.Count == 0);
                return other == null ? null : SExpr.Sym(other);
            default:
                return null;
        }
    }

    private static SExpr Term(Rational value, bool isReal)
    {
        SExpr Whole(BigInteger n) => n.Sign < 0
            ? SExpr.List([SExpr.Sym("-"), SExpr.Atom(isReal ? SExprKind.Decimal : SExprKind.Numeral, (-n) + (isReal ? ".0" : ""))])
            : SExpr.Atom(isReal ? SExprKind.Decimal : SExprKind.Numeral, n + (isReal ? ".0" : ""));

        return value.IsInteger ? Whole(value.Numerator) : SExpr.List([SExpr.Sym("/"), Whole(value.Numerator), Whole(value.Denominator)]);
    }

    private static HashSet<string> Symbols(IEnumerable<SExpr> terms)
    {
        var names = new HashSet<string>();
        var stack = new Stack<SExpr>(terms);
        while (stack.Count > 0)
        {
            var term = stack.Pop();
            if (term.Kind == SExprKind.Symbol)
                names.Add(term.Text);
            foreach (var item in term.Items)
                stack.Push(item);
        }

        return names;
    }

    #endregion
}
