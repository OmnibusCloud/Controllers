namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Arrays: <c>((as const (Array I E)) v)</c>, <c>store</c>, <c>select</c>, and the equality of
/// two arrays, built so that it is often close: the same few indices and values, two defaults
/// that may differ, and for an index sort with few values every index stored. The index sorts
/// are those whose arrays the evaluator compares (Int, Real, String, Bool, bit-vectors of one,
/// two, three and eight bits) and, rarely, an enumeration, whose comparison it leaves open.
/// </summary>
internal sealed class SmtTermFamilyArrays : SmtTermFamily
{
    #region Constants

    private static readonly SmtGenSort[] INDICES =
    [
        SmtGenSort.INT, SmtGenSort.REAL, SmtGenSort.BOOL, SmtGenSort.BitVector(1), SmtGenSort.BitVector(2),
        SmtGenSort.BitVector(3), SmtGenSort.BitVector(8), SmtGenSort.STRING
    ];

    private static readonly SmtGenSort[] ELEMENTS = [SmtGenSort.INT, SmtGenSort.BOOL, SmtGenSort.BitVector(8)];

    private static readonly SmtGenSort NESTED = SmtGenSort.Array(SmtGenSort.INT, SmtGenSort.INT);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    public SmtTermFamilyArrays(SmtTermGenerator generator)
        : base(generator)
    {
    }

    #endregion

    #region Functions

    /// <summary>An array sort of the family's index and element sorts.</summary>
    /// <returns>The sort.</returns>
    public SmtGenSort AnySort()
    {
        var generator = Generator;
        var index = generator.Chance(5) ? SmtTermFamilyDatatypes.COLOR : generator.Pick(INDICES);
        var element = generator.Chance(10) ? NESTED : generator.Pick(ELEMENTS);
        return SmtGenSort.Array(index, element);
    }

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        if (sort.Kind == SmtGenSortKind.Array)
            return Array(sort, depth - 1);

        if (sort.Kind == SmtGenSortKind.Bool && generator.Chance(60))
            return Equality(depth - 1);

        if (!ELEMENTS.Any(element => element.Is(sort)) && !NESTED.Is(sort))
            return null;

        var index = generator.Chance(5) ? SmtTermFamilyDatatypes.COLOR : generator.Pick(INDICES);
        return $"(select {generator.Term(SmtGenSort.Array(index, sort), depth - 1)} {Index(index, depth - 1)})";
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        return sort.Kind == SmtGenSortKind.Array ? $"((as const {sort.Text}) {Small(sort.Element!)})" : null;
    }

    private string Array(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        switch (generator.Next(6))
        {
            case 0:
                return $"((as const {sort.Text}) {Value(sort.Element!, depth)})";
            case 1:
            case 2:
            case 3:
                return $"(store {generator.Term(sort, depth)} {Index(sort.Index!, depth)} {Value(sort.Element!, depth)})";
            default:
                return $"(store (store {generator.Term(sort, depth)} {Index(sort.Index!, depth)} {Value(sort.Element!, depth)}) {Index(sort.Index!, depth)} {Value(sort.Element!, depth)})";
        }
    }

    /// <summary>
    /// Two or three arrays of one sort compared, built from the same few indices and values
    /// over defaults that may differ; for an index sort with few values, every index stored
    /// in one of them, now and then.
    /// </summary>
    private string Equality(int depth)
    {
        var generator = Generator;
        var sort = AnySort();
        var count = generator.Chance(20) ? 3 : 2;
        var arrays = new string[count];
        for (var index = 0; index < count; index++)
        {
            var array = $"((as const {sort.Text}) {Small(sort.Element!)})";
            var everything = Domain(sort.Index!) is { } domain && generator.Chance(50) ? domain : null;
            var stores = everything ?? Enumerable.Range(0, generator.Between(0, 3)).Select(_ => Small(sort.Index!)).ToArray();
            foreach (var stored in stores)
                array = $"(store {array} {stored} {Small(sort.Element!)})";

            arrays[index] = generator.Chance(15) ? generator.Term(sort, depth) : array;
        }

        return $"({(generator.Chance(80) ? "=" : "distinct")} {string.Join(" ", arrays)})";
    }

    private string Index(SmtGenSort sort, int depth)
    {
        return Generator.Chance(60) ? Small(sort) : Generator.Term(sort, depth);
    }

    private string Value(SmtGenSort sort, int depth)
    {
        return Generator.Chance(50) ? Small(sort) : Generator.Term(sort, depth);
    }

    /// <summary>A value from a few of a sort's values, so that indices and values meet often.</summary>
    private string Small(SmtGenSort sort)
    {
        var generator = Generator;
        if (Domain(sort) is { } domain)
            return generator.Pick(domain);

        return sort.Kind switch
        {
            SmtGenSortKind.Int => generator.Pick(["0", "1", "2", "(- 1)"]),
            SmtGenSortKind.Real => generator.Pick(["0.0", "0.5", "1.0", "0.50", "(/ 1.0 2.0)", "(- 0.0)"]),
            SmtGenSortKind.String => generator.Pick(["\"\"", "\"a\"", "\"b\""]),
            SmtGenSortKind.BitVector => generator.Pick(["#x00", "#x01", "#xff"]),
            SmtGenSortKind.Array => $"((as const {sort.Text}) {Small(sort.Element!)})",
            _ => generator.Literal(sort)
        };
    }

    /// <summary>Every value of a sort with few of them; null for a sort with many.</summary>
    private static string[]? Domain(SmtGenSort sort)
    {
        switch (sort.Kind)
        {
            case SmtGenSortKind.Bool:
                return ["true", "false"];
            case SmtGenSortKind.BitVector when sort.Width <= 3:
                return Enumerable.Range(0, 1 << sort.Width).Select(value => $"(_ bv{value} {sort.Width})").ToArray();
            case SmtGenSortKind.Datatype when sort.Is(SmtTermFamilyDatatypes.COLOR):
                return ["red", "green", "blue"];
            default:
                return null;
        }
    }

    #endregion
}
