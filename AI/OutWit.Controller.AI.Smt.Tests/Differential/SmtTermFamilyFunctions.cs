using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Uninterpreted functions and an uninterpreted sort, with the interpretation a model gives
/// them: constants, functions of one and two arguments over Int, Bool, bit-vectors and the
/// sort, whose model is a chain of <c>ite</c> as a solver prints it, and three elements of the
/// sort that the model lists. Each term gets a model of its own.
/// </summary>
internal sealed class SmtTermFamilyFunctions : SmtTermFamily
{
    #region Constants

    /// <summary>The declarations of the sort, the constants and the functions.</summary>
    public const string PRELUDE =
        "(declare-sort U 0)(declare-const x Int)(declare-const y Int)(declare-const b Bool)(declare-const u U)"
        + "(declare-fun f (Int) Int)(declare-fun g (Int Int) Bool)(declare-fun h (Bool Int) Int)(declare-fun k (U) U)"
        + "(declare-fun p (U) Bool)(declare-fun m (Int) U)(declare-fun q (U U) Int)(declare-fun w ((_ BitVec 8)) (_ BitVec 8))";

    /// <summary>The uninterpreted sort.</summary>
    public static readonly SmtGenSort UNIVERSE = SmtGenSort.Uninterpreted("U");

    /// <summary>The elements of the sort, as the model lists them.</summary>
    public static readonly string[] ELEMENTS = ["U!val!0", "U!val!1", "U!val!2"];

    private static readonly SmtGenSort BYTE = SmtGenSort.BitVector(8);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    public SmtTermFamilyFunctions(SmtTermGenerator generator)
        : base(generator)
    {
    }

    #endregion

    #region Functions

    /// <summary>
    /// A model of the prelude, as a solver prints one: the elements, the cardinality
    /// constraint, a value for each constant and a chain of <c>ite</c> for each function.
    /// </summary>
    /// <returns>The answer of <c>get-model</c>.</returns>
    public string NewModel()
    {
        var builder = new StringBuilder("(");
        foreach (var element in ELEMENTS)
            builder.Append($"(declare-fun {element} () U)");

        builder.Append($"(forall ((e U)) (or {string.Join(" ", ELEMENTS.Select(element => $"(= e {element})"))}))");
        builder.Append($"(define-fun x () Int {Small()})(define-fun y () Int {Small()})");
        builder.Append($"(define-fun b () Bool {Truth()})(define-fun u () U {Element()})");
        builder.Append($"(define-fun f ((x!0 Int)) Int {Chain(1, () => [Small()], Small)})");
        builder.Append($"(define-fun g ((x!0 Int) (x!1 Int)) Bool {Chain(2, () => [Small(), Small()], Truth)})");
        builder.Append($"(define-fun h ((x!0 Bool) (x!1 Int)) Int {Chain(2, () => [Truth(), Small()], Small)})");
        builder.Append($"(define-fun k ((x!0 U)) U {Chain(1, () => [Element()], Element)})");
        builder.Append($"(define-fun p ((x!0 U)) Bool {Chain(1, () => [Element()], Truth)})");
        builder.Append($"(define-fun m ((x!0 Int)) U {Chain(1, () => [Small()], Element)})");
        builder.Append($"(define-fun q ((x!0 U) (x!1 U)) Int {Chain(2, () => [Element(), Element()], Small)})");
        builder.Append($"(define-fun w ((x!0 (_ BitVec 8))) (_ BitVec 8) {Chain(1, () => [Byte()], Byte)})");
        return builder.Append(')').ToString();
    }

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        var inner = depth - 1;
        if (sort.Is(UNIVERSE))
            return generator.Chance(50) ? $"(k {Of(inner)})" : $"(m {generator.Term(SmtGenSort.INT, inner)})";

        if (sort.Is(BYTE))
            return $"(w {generator.Term(BYTE, inner)})";

        switch (sort.Kind)
        {
            case SmtGenSortKind.Int:
                return generator.Next(3) switch
                {
                    0 => $"(f {generator.Term(SmtGenSort.INT, inner)})",
                    1 => $"(h {generator.Term(SmtGenSort.BOOL, inner)} {generator.Term(SmtGenSort.INT, inner)})",
                    _ => $"(q {Of(inner)} {Of(inner)})"
                };
            case SmtGenSortKind.Bool:
                return generator.Next(4) switch
                {
                    0 => $"(g {generator.Term(SmtGenSort.INT, inner)} {generator.Term(SmtGenSort.INT, inner)})",
                    1 => $"(p {Of(inner)})",
                    2 => $"(= {generator.Terms(UNIVERSE, inner, 2, 3)})",
                    _ => $"(distinct {generator.Terms(UNIVERSE, inner, 2, 3)})"
                };
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        // The sort has no literals; its constant stands in for one.
        return sort.Is(UNIVERSE) ? "u" : null;
    }

    /// <inheritdoc />
    public override string? Constant(SmtGenSort sort)
    {
        if (sort.Is(UNIVERSE))
            return "u";

        return sort.Kind switch
        {
            SmtGenSortKind.Int => Generator.Pick(["x", "y"]),
            SmtGenSortKind.Bool => "b",
            _ => null
        };
    }

    private string Of(int depth)
    {
        return Generator.Term(UNIVERSE, depth);
    }

    /// <summary>
    /// A chain of <c>ite</c> over the parameters <c>x!0</c>, <c>x!1</c>: a few points, each
    /// with its value, and a value for everything else.
    /// </summary>
    private string Chain(int parameters, Func<string[]> point, Func<string> value)
    {
        var chain = value();
        for (var entry = Generator.Between(0, 3); entry > 0; entry--)
        {
            var at = point();
            var conditions = Enumerable.Range(0, parameters).Select(index => $"(= x!{index} {at[index]})").ToList();
            var condition = conditions.Count == 1 ? conditions[0] : $"(and {string.Join(" ", conditions)})";
            chain = $"(ite {condition} {value()} {chain})";
        }

        return chain;
    }

    private string Small()
    {
        return SmtTermGenerator.Integer(Generator.Between(-3, 3));
    }

    private string Truth()
    {
        return Generator.Chance(50) ? "true" : "false";
    }

    private string Element()
    {
        return Generator.Pick(ELEMENTS);
    }

    private string Byte()
    {
        return Generator.Pick(["#x00", "#x01", "#x7f", "#x80", "#xff", "#x2a"]);
    }

    #endregion
}
