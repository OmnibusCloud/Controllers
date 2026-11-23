namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Ints and Reals: <c>+</c>, <c>-</c> of one argument and of several, <c>*</c>, <c>div</c>,
/// <c>mod</c>, <c>abs</c>, <c>/</c>, <c>to_real</c>, <c>to_int</c>, <c>is_int</c>, and the
/// chains of comparisons. Linear: a product and a division have a constant on one side. A
/// divisor is now and then zero, which the standard leaves to the model. <c>(_ divisible k)</c>
/// is not generated: the solver does not know it (<see cref="SmtDifferentialTests"/> holds that).
/// </summary>
internal sealed class SmtTermFamilyNumbers : SmtTermFamily
{
    #region Constants

    private static readonly string[] COMPARISONS = ["<", "<=", ">", ">="];

    private static readonly string[] REAL_DIVISORS = ["2.0", "0.5", "3.0", "(- 4.0)", "0.125", "7.0", "(- 0.3333)"];

    #endregion

    #region Fields

    private readonly bool m_reals;
    private readonly bool m_nonlinear;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    /// <param name="reals">Whether Reals are generated, beside Ints.</param>
    /// <param name="nonlinear">Whether products and divisions of two terms are generated.</param>
    public SmtTermFamilyNumbers(SmtTermGenerator generator, bool reals, bool nonlinear)
        : base(generator)
    {
        m_reals = reals;
        m_nonlinear = nonlinear;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        return sort.Kind switch
        {
            SmtGenSortKind.Int => Integer(depth - 1),
            SmtGenSortKind.Real when m_reals => Real(depth - 1),
            SmtGenSortKind.Bool => Comparison(depth - 1),
            _ => null
        };
    }

    private string Integer(int depth)
    {
        var generator = Generator;
        var integer = SmtGenSort.INT;
        switch (generator.Next(m_nonlinear ? 12 : (m_reals ? 10 : 9)))
        {
            case 0:
                return $"(+ {generator.Terms(integer, depth, 2, 4)})";
            case 1:
                return $"(- {generator.Term(integer, depth)})";
            case 2:
                return $"(- {generator.Terms(integer, depth, 2, 3)})";
            case 3:
                return $"(* {generator.Literal(integer)} {generator.Term(integer, depth)})";
            case 4:
                return $"(div {generator.Term(integer, depth)} {Divisor()})";
            case 5:
                return $"(mod {generator.Term(integer, depth)} {Divisor()})";
            case 6:
                return $"(abs {generator.Term(integer, depth)})";
            case 7:
                return $"(div {generator.Term(integer, depth)} {Divisor()} {Divisor()})";
            case 8:
                return $"(* {generator.Term(integer, depth)} {generator.Literal(integer)})";
            case 9 when m_reals:
                return $"(to_int {generator.Term(SmtGenSort.REAL, depth)})";
            case 9:
            case 10:
                return $"(* {generator.Terms(integer, depth, 2, 3)})";
            default:
                return $"({generator.Pick(["div", "mod"])} {generator.Term(integer, depth)} {generator.Term(integer, depth)})";
        }
    }

    private string Real(int depth)
    {
        var generator = Generator;
        var real = SmtGenSort.REAL;
        switch (generator.Next(m_nonlinear ? 10 : 8))
        {
            case 0:
                return $"(+ {generator.Terms(real, depth, 2, 4)})";
            case 1:
                return $"(- {generator.Term(real, depth)})";
            case 2:
                return $"(- {generator.Terms(real, depth, 2, 3)})";
            case 3:
                return $"(* {generator.Literal(real)} {generator.Term(real, depth)})";
            case 4:
                return $"(/ {generator.Term(real, depth)} {RealDivisor()})";
            case 5:
                return $"(to_real {generator.Term(SmtGenSort.INT, depth)})";
            case 6:
                return $"(/ {generator.Term(real, depth)} {RealDivisor()} {RealDivisor()})";
            case 7:
                return $"(* {generator.Term(real, depth)} {generator.Literal(real)})";
            case 8:
                return $"(* {generator.Terms(real, depth, 2, 3)})";
            default:
                return $"(/ {generator.Term(real, depth)} {generator.Term(real, depth)})";
        }
    }

    private string Comparison(int depth)
    {
        var generator = Generator;
        var sort = m_reals && generator.Chance(60) ? SmtGenSort.REAL : SmtGenSort.INT;
        switch (generator.Next(5))
        {
            case 0:
            case 1:
            case 2:
                return $"({generator.Pick(COMPARISONS)} {generator.Terms(sort, depth, 2, 4)})";
            case 3 when m_reals:
                return $"(is_int {generator.Term(SmtGenSort.REAL, depth)})";
            default:
                // The divisibility the solver has no operator for ((_ divisible k) is unknown to it), written out.
                return $"(= (mod {generator.Term(SmtGenSort.INT, depth)} {generator.Between(1, 12)}) 0)";
        }
    }

    /// <summary>A constant divisor of an integer: now and then zero, at times negative.</summary>
    private string Divisor()
    {
        var generator = Generator;
        if (generator.Chance(12))
            return "0";

        var value = generator.Chance(80) ? generator.Between(1, 9) : generator.IntegerValue();
        if (value.IsZero)
            value = 3;

        return SmtTermGenerator.Integer(generator.Chance(30) ? -value : value);
    }

    /// <summary>A constant divisor of a real: now and then zero.</summary>
    private string RealDivisor()
    {
        return Generator.Chance(10) ? "0.0" : Generator.Pick(REAL_DIVISORS);
    }

    #endregion
}
