using System.Globalization;
using System.Numerics;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Generates closed terms of a theory, from a seed: the same seed gives the same terms on every
/// machine. A term is generated for a sort, top down: an <c>ite</c>, a <c>let</c>, a connective,
/// an operator of one of the theory's families (<see cref="SmtTermFamily"/>), or a leaf, which
/// is a literal, a variable a <c>let</c> or a <c>match</c> bound, or a constant the prelude
/// declares. The edges of every operator are asked for on purpose: zero, one, minus one, the
/// largest and the most negative value, a shift by more than the width, a position outside a
/// string, a selector of another constructor.
/// </summary>
internal sealed class SmtTermGenerator
{
    #region Constants

    /// <summary>How many levels a generated term nests at most.</summary>
    public const int DEPTH = 4;

    private const string LOGIC = "(set-logic ALL)";

    private static readonly string[] VARIABLES = ["v0", "v1", "v2"];

    private static readonly string[] BIG_INTEGERS = ["18446744073709551617", "340282366920938463463374607431768211455", "1000000000000000000000000000001"];

    private static readonly string[] DECIMALS = ["0.0", "1.0", "2.5", "0.125", "3.75", "10.0", "0.3333", "1000000.000001", "7.0"];

    #endregion

    #region Fields

    private readonly Random m_random;
    private readonly List<(string Name, SmtGenSort Sort)> m_bound = [];
    private readonly SmtTermFamilyNumbers m_intLinear;
    private readonly SmtTermFamilyNumbers m_intNonlinear;
    private readonly SmtTermFamilyNumbers m_realLinear;
    private readonly SmtTermFamilyNumbers m_realNonlinear;
    private readonly SmtTermFamilyBitVectors m_bitVectors;
    private readonly SmtTermFamilyStrings m_strings;
    private readonly SmtTermFamilyArrays m_arrays;
    private readonly SmtTermFamilyDatatypes m_datatypes;
    private readonly SmtTermFamilyFunctions m_functions;
    private SmtTermFamily[] m_families = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a generator.
    /// </summary>
    /// <param name="seed">The seed of its random choices.</param>
    public SmtTermGenerator(int seed)
    {
        m_random = new Random(seed);
        m_intLinear = new SmtTermFamilyNumbers(this, reals: false, nonlinear: false);
        m_intNonlinear = new SmtTermFamilyNumbers(this, reals: false, nonlinear: true);
        m_realLinear = new SmtTermFamilyNumbers(this, reals: true, nonlinear: false);
        m_realNonlinear = new SmtTermFamilyNumbers(this, reals: true, nonlinear: true);
        m_bitVectors = new SmtTermFamilyBitVectors(this);
        m_strings = new SmtTermFamilyStrings(this);
        m_arrays = new SmtTermFamilyArrays(this);
        m_datatypes = new SmtTermFamilyDatatypes(this);
        m_functions = new SmtTermFamilyFunctions(this);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Generates the next term of a theory.
    /// </summary>
    /// <param name="theory">The theory.</param>
    /// <param name="number">The term's number in its theory.</param>
    /// <returns>The term with what it needs.</returns>
    public SmtGeneratedTerm Next(SmtDifferentialTheory theory, int number)
    {
        m_bound.Clear();
        SmtGenSort[] tops;
        var prelude = LOGIC;
        string? model = null;
        IReadOnlyList<IReadOnlyList<string>> elements = [];
        switch (theory)
        {
            case SmtDifferentialTheory.IntLinear:
                m_families = [m_intLinear];
                EqualSorts = [SmtGenSort.INT, SmtGenSort.BOOL];
                tops = [SmtGenSort.INT, SmtGenSort.INT, SmtGenSort.BOOL];
                break;
            case SmtDifferentialTheory.IntNonlinear:
                m_families = [m_intNonlinear];
                EqualSorts = [SmtGenSort.INT, SmtGenSort.BOOL];
                tops = [SmtGenSort.INT, SmtGenSort.INT, SmtGenSort.BOOL];
                break;
            case SmtDifferentialTheory.RealLinear:
                m_families = [m_realLinear];
                EqualSorts = [SmtGenSort.REAL, SmtGenSort.INT];
                tops = [SmtGenSort.REAL, SmtGenSort.REAL, SmtGenSort.BOOL, SmtGenSort.INT];
                break;
            case SmtDifferentialTheory.RealNonlinear:
                m_families = [m_realNonlinear];
                EqualSorts = [SmtGenSort.REAL, SmtGenSort.INT];
                tops = [SmtGenSort.REAL, SmtGenSort.REAL, SmtGenSort.BOOL];
                break;
            case SmtDifferentialTheory.BitVectors:
                m_families = [m_bitVectors];
                EqualSorts = [SmtGenSort.BitVector(8), SmtGenSort.BitVector(1), SmtGenSort.BitVector(65)];
                tops = [.. SmtTermFamilyBitVectors.WIDTHS.Select(SmtGenSort.BitVector), SmtGenSort.BOOL, SmtGenSort.BOOL, SmtGenSort.INT];
                break;
            case SmtDifferentialTheory.Arrays:
                m_families = [m_arrays];
                prelude += SmtTermFamilyDatatypes.PRELUDE;
                EqualSorts = [SmtGenSort.INT, SmtGenSort.BOOL];
                tops = [m_arrays.AnySort(), m_arrays.AnySort(), SmtGenSort.BOOL, SmtGenSort.BOOL, SmtGenSort.INT];
                break;
            case SmtDifferentialTheory.Datatypes:
                m_families = [m_datatypes, m_intLinear];
                prelude += SmtTermFamilyDatatypes.PRELUDE;
                EqualSorts = [SmtTermFamilyDatatypes.LIST, SmtTermFamilyDatatypes.TREE, SmtTermFamilyDatatypes.COLOR, SmtGenSort.INT];
                tops = [SmtTermFamilyDatatypes.LIST, SmtTermFamilyDatatypes.TREE, SmtTermFamilyDatatypes.COLOR, SmtGenSort.INT, SmtGenSort.BOOL];
                break;
            case SmtDifferentialTheory.Functions:
                m_families = [m_functions, m_intLinear];
                prelude += SmtTermFamilyFunctions.PRELUDE;
                model = m_functions.NewModel();
                elements = [SmtTermFamilyFunctions.ELEMENTS, SmtTermFamilyFunctions.UNLISTED_ELEMENTS];
                EqualSorts = [SmtGenSort.INT, SmtGenSort.BOOL, SmtTermFamilyFunctions.UNIVERSE, SmtTermFamilyFunctions.UNLISTED];
                tops = [SmtGenSort.INT, SmtGenSort.BOOL, SmtGenSort.BitVector(8)];
                break;
            case SmtDifferentialTheory.Strings:
                m_families = [m_strings, m_intLinear];
                EqualSorts = [SmtGenSort.STRING, SmtGenSort.INT];
                tops = [SmtGenSort.STRING, SmtGenSort.STRING, SmtGenSort.INT, SmtGenSort.BOOL];
                break;
            default:
                m_families = [m_intLinear, m_realLinear];
                EqualSorts = [SmtGenSort.INT, SmtGenSort.BOOL, SmtGenSort.REAL, SmtGenSort.STRING, SmtGenSort.BitVector(8)];
                tops = [SmtGenSort.BOOL, SmtGenSort.BOOL, SmtGenSort.INT];
                break;
        }

        var sort = Pick(tops);
        return new SmtGeneratedTerm
        {
            Theory = theory, Number = number, Prelude = prelude, Model = model, Elements = elements,
            Term = Term(sort, DEPTH), Sort = sort
        };
    }

    /// <summary>
    /// Generates a term of a sort.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <param name="depth">How many levels the term may still nest.</param>
    /// <returns>The term.</returns>
    public string Term(SmtGenSort sort, int depth)
    {
        if (depth <= 0 || Chance(15))
            return Leaf(sort);

        var roll = Next(100);
        if (roll < 8)
            return $"(ite {Term(SmtGenSort.BOOL, depth - 1)} {Term(sort, depth - 1)} {Term(sort, depth - 1)})";

        if (roll < 14)
            return Let(sort, depth);

        if (sort.Kind == SmtGenSortKind.Bool && roll < 40)
            return Connective(depth);

        foreach (var family in m_families.OrderBy(_ => Next(1000)).ToList())
        {
            if (family.Produce(sort, depth) is { } term)
                return term;
        }

        return sort.Kind == SmtGenSortKind.Bool ? Connective(depth) : Leaf(sort);
    }

    /// <summary>
    /// Generates several terms of one sort, separated by blanks.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <param name="depth">How many levels each may still nest.</param>
    /// <param name="least">The fewest terms.</param>
    /// <param name="most">The most terms.</param>
    /// <returns>The terms.</returns>
    public string Terms(SmtGenSort sort, int depth, int least, int most)
    {
        var count = Between(least, most);
        var terms = new string[count];
        for (var index = 0; index < count; index++)
            terms[index] = Term(sort, depth);

        return string.Join(" ", terms);
    }

    /// <summary>
    /// A literal of a sort.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The literal.</returns>
    public string Literal(SmtGenSort sort)
    {
        switch (sort.Kind)
        {
            case SmtGenSortKind.Bool:
                return Chance(50) ? "true" : "false";
            case SmtGenSortKind.Int:
                return Integer(IntegerValue());
            case SmtGenSortKind.Real:
                return RealLiteral();
            case SmtGenSortKind.BitVector:
                return m_bitVectors.Literal(sort, Edge(sort.Width));
            case SmtGenSortKind.String:
                return m_strings.Literal(sort)!;
            case SmtGenSortKind.Array:
                return m_arrays.Literal(sort)!;
            case SmtGenSortKind.Datatype:
                return m_datatypes.Literal(sort)!;
            default:
                return m_functions.Literal(sort)!;
        }
    }

    /// <summary>
    /// A random integer: mostly small, at times of a few hundred, at times past 64 bits.
    /// </summary>
    /// <returns>The integer.</returns>
    public BigInteger IntegerValue()
    {
        var roll = Next(100);
        if (roll < 60)
            return Between(-6, 6);

        if (roll < 85)
            return Between(-1000, 1000);

        var big = BigInteger.Parse(Pick(BIG_INTEGERS), CultureInfo.InvariantCulture);
        return Chance(50) ? -big : big;
    }

    /// <summary>
    /// An integer as SMT-LIB writes it: a numeral, or the negation of one.
    /// </summary>
    /// <param name="value">The integer.</param>
    /// <returns>The term.</returns>
    public static string Integer(BigInteger value)
    {
        return value.Sign < 0 ? $"(- {(-value).ToString(CultureInfo.InvariantCulture)})" : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A value of a width with a fair chance of being an edge: zero, one, all ones, the most
    /// negative, the largest positive; otherwise random bits.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <returns>The unsigned value.</returns>
    public BigInteger Edge(int width)
    {
        var mask = (BigInteger.One << width) - 1;
        var top = BigInteger.One << (width - 1);
        return Next(8) switch
        {
            0 => BigInteger.Zero,
            1 => BigInteger.One & mask,
            2 => mask,
            3 => top,
            4 => top - 1,
            _ => RandomBits(width)
        };
    }

    /// <summary>
    /// A random value of a width.
    /// </summary>
    /// <param name="width">The width.</param>
    /// <returns>The unsigned value.</returns>
    public BigInteger RandomBits(int width)
    {
        var bytes = new byte[width / 8 + 2];
        m_random.NextBytes(bytes);
        bytes[^1] = 0;
        return new BigInteger(bytes) & ((BigInteger.One << width) - 1);
    }

    /// <summary>Binds a variable for the terms generated until <see cref="Unbind"/>: a pattern's variable.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="sort">Its sort.</param>
    public void Bind(string name, SmtGenSort sort)
    {
        m_bound.Add((name, sort));
    }

    /// <summary>Ends the last bindings.</summary>
    /// <param name="count">How many.</param>
    public void Unbind(int count)
    {
        m_bound.RemoveRange(m_bound.Count - count, count);
    }

    /// <summary>A random number from 0 up to, not including, a bound.</summary>
    /// <param name="bound">The bound.</param>
    /// <returns>The number.</returns>
    public int Next(int bound)
    {
        return m_random.Next(bound);
    }

    /// <summary>A random number between two, both included.</summary>
    /// <param name="least">The least.</param>
    /// <param name="most">The most.</param>
    /// <returns>The number.</returns>
    public int Between(int least, int most)
    {
        return m_random.Next(least, most + 1);
    }

    /// <summary>Whether a chance of so many in a hundred came up.</summary>
    /// <param name="percent">The chance.</param>
    /// <returns>True when it came up.</returns>
    public bool Chance(int percent)
    {
        return m_random.Next(100) < percent;
    }

    /// <summary>One of several.</summary>
    /// <param name="items">The choices.</param>
    /// <returns>The one picked.</returns>
    public T Pick<T>(IReadOnlyList<T> items)
    {
        return items[m_random.Next(items.Count)];
    }

    private string Leaf(SmtGenSort sort)
    {
        // A name bound twice is the inner binding: only that one is visible.
        var visible = m_bound
            .Select((binding, index) => (binding.Name, binding.Sort, Index: index))
            .Where(binding => m_bound.FindLastIndex(other => other.Name == binding.Name) == binding.Index && binding.Sort.Is(sort))
            .ToList();
        if (visible.Count > 0 && Chance(40))
            return Pick(visible).Name;

        if (Chance(40))
        {
            foreach (var family in m_families)
            {
                if (family.Constant(sort) is { } constant)
                    return constant;
            }
        }

        return Literal(sort);
    }

    private string Let(SmtGenSort sort, int depth)
    {
        var count = Between(1, 2);
        var names = VARIABLES.OrderBy(_ => Next(1000)).Take(count).ToList();
        var bindings = new List<string>(count);
        var sorts = new List<SmtGenSort>(count);

        // Every value is generated among the names outside the let: the bindings are parallel.
        foreach (var name in names)
        {
            var bound = Chance(50) ? sort : Pick(EqualSorts);
            sorts.Add(bound);
            bindings.Add($"({name} {Term(bound, depth - 1)})");
        }

        for (var index = 0; index < count; index++)
            Bind(names[index], sorts[index]);

        var body = Term(sort, depth - 1);
        Unbind(count);
        return $"(let ({string.Join(" ", bindings)}) {body})";
    }

    private string Connective(int depth)
    {
        var inner = depth - 1;
        var bools = SmtGenSort.BOOL;
        switch (Next(8))
        {
            case 0:
                return $"(not {Term(bools, inner)})";
            case 1:
                return $"(and {Terms(bools, inner, 2, 4)})";
            case 2:
                return $"(or {Terms(bools, inner, 2, 4)})";
            case 3:
                return $"(xor {Terms(bools, inner, 2, 3)})";
            case 4:
                return $"(=> {Terms(bools, inner, 2, 3)})";
            case 5:
                var equal = Pick(EqualSorts);
                return $"(= {Terms(equal, inner, 2, 3)})";
            case 6:
                // Past eight values the evaluator sorts them instead of comparing each pair.
                var distinct = Pick(EqualSorts);
                return Chance(20) ? $"(distinct {Terms(distinct, Math.Min(inner, 1), 9, 12)})" : $"(distinct {Terms(distinct, inner, 2, 4)})";
            default:
                return Literal(bools);
        }
    }

    private string RealLiteral()
    {
        var roll = Next(10);
        string literal;
        if (roll < 6)
            literal = Pick(DECIMALS);
        else
        {
            var value = BigInteger.Abs(IntegerValue());
            literal = value.ToString(CultureInfo.InvariantCulture) + ".0";
        }

        return Chance(30) ? $"(- {literal})" : literal;
    }

    #endregion

    #region Properties

    /// <summary>The sorts of the theory at hand that equalities, distinctions and bindings are made over.</summary>
    public SmtGenSort[] EqualSorts { get; private set; } = [];

    #endregion
}
