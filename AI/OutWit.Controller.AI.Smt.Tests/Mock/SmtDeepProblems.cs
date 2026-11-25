using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Problems that are small in everything but their nesting: one assertion whose term is a chain,
/// each link inside the one before, as deep as it is asked for. The reader takes a problem nested
/// up to 100,000 lists deep, and the solver's own code walks such a term by calling itself, a
/// level a call: how deep it can go is the native stack the sandbox gives a guest, counted in
/// bytes, and the bytes of one call are the compiler's for the machine. So these are the problems
/// on which two machines could part: the tests solve them at four depths up to the reader's own.
/// <para>
/// A depth here is the reader's: the nesting of the deepest list of the text, a top-level command
/// at depth 1. Written for these tests; none is a benchmark file.
/// </para>
/// </summary>
internal static class SmtDeepProblems
{
    #region Constants

    /// <summary>The shape of a chain of additions: <c>(+ 1 (+ 1 ... y))</c>.</summary>
    public const string PLUS = "plus";

    /// <summary>The shape of a chain of connectives, a conjunction and a disjunction in turn: <c>(and p (or q (and p ... p)))</c>.</summary>
    public const string AND_OR = "and-or";

    /// <summary>The shape of a chain of bindings, each in the body of the one before: <c>(let ((v (+ y 1))) (let ((v (+ v 1))) ... v))</c>.</summary>
    public const string LET = "let";

    /// <summary>The shape of a chain of conditionals, each the last branch of the one before: <c>(ite c 1 (ite c 1 ... 0))</c>.</summary>
    public const string ITE = "ite";

    /// <summary>The shapes, in the order of the cases.</summary>
    public static readonly IReadOnlyList<string> SHAPES = [PLUS, AND_OR, LET, ITE];

    /// <summary>The depths, in the order of the cases: the last is the deepest nesting the reader takes.</summary>
    public static readonly IReadOnlyList<int> DEPTHS = [25_000, 50_000, 75_000, 100_000];

    // How much deeper than the first link of its chain the deepest list of a problem lies, and
    // how deep that first link stands in its assertion: (assert (= x CHAIN)) puts it at 3,
    // (assert CHAIN) at 2; the term a let binds stands three lists inside its let.
    private const int FIRST_LINK_IN_AN_EQUATION = 3;
    private const int FIRST_LINK_OF_AN_ASSERTION = 2;
    private const int BOUND_TERM_IN_ITS_LET = 3;

    private const char CLOSE = ')';

    #endregion

    #region Functions

    /// <summary>
    /// The problem of a shape at a depth.
    /// </summary>
    /// <param name="shape">One of <see cref="SHAPES"/>.</param>
    /// <param name="depth">The nesting of the problem's deepest list, as the reader counts it.</param>
    /// <returns>The problem's text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The shape is none of the four, or the depth is too small for a chain of one link.</exception>
    public static string Of(string shape, int depth)
    {
        return shape switch
        {
            PLUS => Chain("(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= x ", _ => "(+ 1 ", "y", "))", depth - FIRST_LINK_IN_AN_EQUATION + 1, depth),
            AND_OR => Chain("(set-logic QF_UF)(declare-const p Bool)(declare-const q Bool)(assert ", link => link % 2 == 0 ? "(and p " : "(or q ", "p", ")", depth - FIRST_LINK_OF_AN_ASSERTION + 1, depth),
            LET => Chain("(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= x ", link => link == 0 ? "(let ((v (+ y 1))) " : "(let ((v (+ v 1))) ", "v", "))",
                depth - FIRST_LINK_IN_AN_EQUATION - BOUND_TERM_IN_ITS_LET + 1, depth),
            ITE => Chain("(set-logic QF_LIA)(declare-const x Int)(declare-const c Bool)(assert (= x ", _ => "(ite c 1 ", "0", "))", depth - FIRST_LINK_IN_AN_EQUATION + 1, depth),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "no shape of a deep problem")
        };
    }

    /// <summary>The name of the case of a shape at a depth, as the golden files have it.</summary>
    /// <param name="shape">One of <see cref="SHAPES"/>.</param>
    /// <param name="depth">The depth.</param>
    /// <returns>The name.</returns>
    public static string Id(string shape, int depth)
    {
        return $"check-deep-{shape}-{depth}";
    }

    #endregion

    #region Tools

    private static string Chain(string before, Func<int, string> open, string innermost, string after, int links, int depth)
    {
        if (links < 1)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "too shallow for a chain of one link");

        var builder = new StringBuilder(before);
        for (var link = 0; link < links; link++)
            builder.Append(open(link));

        return builder.Append(innermost).Append(CLOSE, links).Append(after).ToString();
    }

    #endregion
}
