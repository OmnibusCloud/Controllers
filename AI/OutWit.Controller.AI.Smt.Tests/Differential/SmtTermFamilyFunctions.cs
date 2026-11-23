using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Uninterpreted functions and two uninterpreted sorts, with the interpretation a model gives
/// them: constants, functions of one and two arguments over Int, Bool, bit-vectors and the
/// sorts, whose model is a chain of <c>ite</c> as a solver prints it. The model lists the three
/// elements of the sort <c>U</c>; it names the three of <c>V</c> without listing them, as a
/// solver may. Each term gets a model of its own.
/// </summary>
internal sealed class SmtTermFamilyFunctions : SmtTermFamily
{
    #region Constants

    /// <summary>The declarations of the sorts, the constants and the functions.</summary>
    public const string PRELUDE =
        "(declare-sort U 0)(declare-const x Int)(declare-const y Int)(declare-const b Bool)(declare-const u U)"
        + "(declare-fun f (Int) Int)(declare-fun g (Int Int) Bool)(declare-fun h (Bool Int) Int)(declare-fun k (U) U)"
        + "(declare-fun p (U) Bool)(declare-fun m (Int) U)(declare-fun q (U U) Int)(declare-fun w ((_ BitVec 8)) (_ BitVec 8))"
        + "(declare-sort V 0)(declare-const v V)(declare-fun r (Int) V)(declare-fun s (V) Int)(declare-fun t (V U) V)";

    /// <summary>The uninterpreted sort whose universe the model lists.</summary>
    public static readonly SmtGenSort UNIVERSE = SmtGenSort.Uninterpreted("U");

    /// <summary>The elements of <see cref="UNIVERSE"/>, as the model lists them.</summary>
    public static readonly string[] ELEMENTS = ["U!val!0", "U!val!1", "U!val!2"];

    /// <summary>The uninterpreted sort whose elements the model names without listing them.</summary>
    public static readonly SmtGenSort UNLISTED = SmtGenSort.Uninterpreted("V");

    /// <summary>The elements of <see cref="UNLISTED"/>, as the model names them; every model names all three.</summary>
    public static readonly string[] UNLISTED_ELEMENTS = ["V!val!0", "V!val!1", "V!val!2"];

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
    /// A model of the prelude, as a solver prints one: the elements of <c>U</c>, the cardinality
    /// constraint, a value for each constant and a chain of <c>ite</c> for each function. The
    /// elements of <c>V</c> are named, not listed; the interpretation of <c>s</c> names all three.
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
        builder.Append($"(define-fun v () V {Hidden()})");
        builder.Append($"(define-fun r ((x!0 Int)) V {Chain(1, () => [Small()], Hidden)})");
        builder.Append($"(define-fun s ((x!0 V)) Int (ite (= x!0 V!val!0) {Small()} (ite (= x!0 V!val!1) {Small()} (ite (= x!0 V!val!2) {Small()} {Small()}))))");
        builder.Append($"(define-fun t ((x!0 V) (x!1 U)) V {Chain(2, () => [Hidden(), Element()], Hidden)})");
        return builder.Append(')').ToString();
    }

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        var inner = depth - 1;
        if (sort.Is(UNIVERSE))
            return generator.Chance(50) ? $"(k {Of(inner)})" : $"(m {generator.Term(SmtGenSort.INT, inner)})";

        if (sort.Is(UNLISTED))
            return generator.Chance(50) ? $"(r {generator.Term(SmtGenSort.INT, inner)})" : $"(t {OfUnlisted(inner)} {Of(inner)})";

        if (sort.Is(BYTE))
            return $"(w {generator.Term(BYTE, inner)})";

        switch (sort.Kind)
        {
            case SmtGenSortKind.Int:
                return generator.Next(4) switch
                {
                    0 => $"(f {generator.Term(SmtGenSort.INT, inner)})",
                    1 => $"(h {generator.Term(SmtGenSort.BOOL, inner)} {generator.Term(SmtGenSort.INT, inner)})",
                    2 => $"(s {OfUnlisted(inner)})",
                    _ => $"(q {Of(inner)} {Of(inner)})"
                };
            case SmtGenSortKind.Bool:
                var compared = generator.Chance(50) ? UNIVERSE : UNLISTED;
                return generator.Next(4) switch
                {
                    0 => $"(g {generator.Term(SmtGenSort.INT, inner)} {generator.Term(SmtGenSort.INT, inner)})",
                    1 => $"(p {Of(inner)})",
                    2 => $"(= {generator.Terms(compared, inner, 2, 3)})",
                    _ => $"(distinct {generator.Terms(compared, inner, 2, 3)})"
                };
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        // The sorts have no literals; their constants stand in for one.
        if (sort.Is(UNIVERSE))
            return "u";

        return sort.Is(UNLISTED) ? "v" : null;
    }

    /// <inheritdoc />
    public override string? Constant(SmtGenSort sort)
    {
        if (sort.Is(UNIVERSE))
            return "u";

        if (sort.Is(UNLISTED))
            return "v";

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

    private string OfUnlisted(int depth)
    {
        return Generator.Term(UNLISTED, depth);
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

    private string Hidden()
    {
        return Generator.Pick(UNLISTED_ELEMENTS);
    }

    private string Byte()
    {
        return Generator.Pick(["#x00", "#x01", "#x7f", "#x80", "#xff", "#x2a"]);
    }

    #endregion
}
