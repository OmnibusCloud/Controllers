namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Datatypes: a list of integers, a tree that holds lists, and an enumeration. Constructors,
/// selectors (now and then of another constructor than the value's, which the standard leaves
/// to the model), testers, equality, and <c>match</c> with constructor patterns, variables and
/// a catch-all variable.
/// </summary>
internal sealed class SmtTermFamilyDatatypes : SmtTermFamily
{
    #region Constants

    /// <summary>The declarations of the datatypes.</summary>
    public const string PRELUDE =
        "(declare-datatypes ((IntList 0) (Tree 0)) (((nil) (cons (head Int) (tail IntList))) ((leaf (val Int)) (node (left Tree) (items IntList) (right Tree)))))"
        + "(declare-datatype Color ((red) (green) (blue)))";

    /// <summary>The list of integers.</summary>
    public static readonly SmtGenSort LIST = SmtGenSort.Datatype("IntList");

    /// <summary>The tree.</summary>
    public static readonly SmtGenSort TREE = SmtGenSort.Datatype("Tree");

    /// <summary>The enumeration.</summary>
    public static readonly SmtGenSort COLOR = SmtGenSort.Datatype("Color");

    private static readonly string[] COLORS = ["red", "green", "blue"];

    private static readonly string[] PATTERN_VARIABLES = ["p0", "p1", "p2", "v0"];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    public SmtTermFamilyDatatypes(SmtTermGenerator generator)
        : base(generator)
    {
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        var inner = depth - 1;
        if (sort.Is(LIST))
        {
            return generator.Next(5) switch
            {
                0 or 1 => $"(cons {generator.Term(SmtGenSort.INT, inner)} {generator.Term(LIST, inner)})",
                2 => Selector("tail", "cons", LIST, sort, inner),
                3 => Selector("items", "node", TREE, sort, inner),
                _ => Match(sort, inner)
            };
        }

        if (sort.Is(TREE))
        {
            return generator.Next(5) switch
            {
                0 => $"(leaf {generator.Term(SmtGenSort.INT, inner)})",
                1 or 2 => $"(node {generator.Term(TREE, inner)} {generator.Term(LIST, inner)} {generator.Term(TREE, inner)})",
                3 => Selector(generator.Pick(["left", "right"]), "node", TREE, sort, inner),
                _ => Match(sort, inner)
            };
        }

        if (sort.Is(COLOR))
            return Match(sort, inner);

        switch (sort.Kind)
        {
            case SmtGenSortKind.Int:
                return generator.Next(3) switch
                {
                    0 => Selector("head", "cons", LIST, sort, inner),
                    1 => Selector("val", "leaf", TREE, sort, inner),
                    _ => Match(sort, inner)
                };
            case SmtGenSortKind.Bool:
                return generator.Next(4) switch
                {
                    0 => $"((_ is {generator.Pick(["nil", "cons"])}) {generator.Term(LIST, inner)})",
                    1 => $"((_ is {generator.Pick(["leaf", "node"])}) {generator.Term(TREE, inner)})",
                    2 => $"((_ is {generator.Pick(COLORS)}) {generator.Term(COLOR, inner)})",
                    _ => $"(= {generator.Terms(generator.Pick([LIST, TREE, COLOR]), inner, 2, 3)})"
                };
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        var generator = Generator;
        if (sort.Is(LIST))
            return generator.Chance(60) ? "nil" : $"(cons {generator.Literal(SmtGenSort.INT)} nil)";

        if (sort.Is(TREE))
            return $"(leaf {generator.Literal(SmtGenSort.INT)})";

        return sort.Is(COLOR) ? generator.Pick(COLORS) : null;
    }

    /// <summary>
    /// A match of a value of one of the datatypes, each case's body of the sort asked for; the
    /// variables of a pattern are bound in its body.
    /// </summary>
    private string Match(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        var names = PATTERN_VARIABLES.OrderBy(_ => generator.Next(1000)).ToArray();
        switch (generator.Next(3))
        {
            case 0:
                var list = generator.Term(LIST, depth);
                var empty = Case("nil", [], sort, depth);
                var cons = Case($"(cons {names[0]} {names[1]})", [(names[0], SmtGenSort.INT), (names[1], LIST)], sort, depth);
                var rest = Case(names[2], [(names[2], LIST)], sort, depth);
                return generator.Next(3) switch
                {
                    0 => $"(match {list} ({empty} {cons}))",
                    1 => $"(match {list} ({cons} {rest}))",
                    _ => $"(match {list} ({cons} {empty}))"
                };
            case 1:
                var tree = generator.Term(TREE, depth);
                var leaf = Case($"(leaf {names[0]})", [(names[0], SmtGenSort.INT)], sort, depth);
                var node = Case($"(node {names[0]} {names[1]} {names[2]})", [(names[0], TREE), (names[1], LIST), (names[2], TREE)], sort, depth);
                return $"(match {tree} ({leaf} {node}))";
            default:
                var color = generator.Term(COLOR, depth);
                var first = generator.Pick(COLORS);
                var cases = generator.Chance(50)
                    ? string.Join(" ", COLORS.Select(name => Case(name, [], sort, depth)))
                    : $"{Case(first, [], sort, depth)} {Case(names[0], [(names[0], COLOR)], sort, depth)}";
                return $"(match {color} ({cases}))";
        }
    }

    /// <summary>
    /// A selector applied to a value: as it is, at times of another constructor than the
    /// value's; or, as problems write it, behind its tester, the value bound once by a let.
    /// </summary>
    private string Selector(string selector, string constructor, SmtGenSort datatype, SmtGenSort sort, int depth)
    {
        var generator = Generator;
        var value = generator.Term(datatype, depth);
        if (generator.Chance(40))
            return $"({selector} {value})";

        return $"(let ((s0 {value})) (ite ((_ is {constructor}) s0) ({selector} s0) {generator.Term(sort, depth)}))";
    }

    private string Case(string pattern, (string Name, SmtGenSort Sort)[] variables, SmtGenSort sort, int depth)
    {
        foreach (var (name, bound) in variables)
            Generator.Bind(name, bound);

        var body = Generator.Term(sort, depth);
        Generator.Unbind(variables.Length);
        return $"({pattern} {body})";
    }

    #endregion
}
