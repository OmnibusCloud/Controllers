using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The sorts an enumeration can be projected on, walked through the real solver module
/// (<see cref="SmtProjectionCases"/>). An enumeration rules a found model out by saying the
/// projected constants' values back to the solver, which works exactly when the solver reads
/// what it printed.
/// <list type="bullet">
/// <item>An enumeration whose values the solver reads back goes as far as it is asked: at least
/// two models are found and told apart wherever the problem has two, each case to the words
/// written beside it.</item>
/// <item>A value the solver does not read back ends the enumeration there. The models found are
/// whole and true and are kept; that there is no other is not known, and the row says so: it is
/// an answer (<c>ok</c>, <c>sat</c>), not complete, for the reason <c>other</c>, with the
/// solver's own words. The pinned solver has two such forms. It names the elements of a sort the
/// problem only declares by names of its own (<c>U!val!0</c>) and takes none of them in a later
/// command; and it prints an array that is the identity as a function,
/// <c>(lambda ((x!1 ...)) x!1)</c>, which a quantifier-free logic does not take.</item>
/// <item>No sort is refused for that. The rows over a declared sort that never had a model to
/// rule out (one model asked for, a problem with no model, values that hold no element of the
/// sort) end as they always did, to the digest: <see cref="ENDED_WELL"/> holds the rows a node
/// gave for them before an enumeration could end as above.</item>
/// </list>
/// Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverProjectionTests
{
    #region Constants

    private const string NOT_READ_BACK = "ok sat other 4 incomplete";

    /// <summary>
    /// The arrays from one bit to one bit, under a logic without quantifiers: there are four, and
    /// the solver prints one of them, the identity, as a function, which that logic does not take
    /// when the value is said back.
    /// </summary>
    private const string IDENTITY_ARRAYS = "(set-logic QF_ABV)(declare-const a (Array (_ BitVec 1) (_ BitVec 1)))";

    /// <summary>
    /// The digest of the row a node returns for the enumeration of <see cref="IDENTITY_ARRAYS"/>
    /// (the problem at index 0, up to eight models): the row is the same on every platform, and
    /// the workflow runs this fixture on Windows, Linux and macOS.
    /// </summary>
    private const string IDENTITY_ARRAYS_ROW = "6d0543f9b531c0678ee16ba32b2a700f5f4d89d2652d30851f5ac0b05164368b";

    /// <summary>A constant of a sort the problem only declares, and two models asked for: the second cannot be asked for.</summary>
    private const string DECLARED_SORT = "(declare-sort U 0)(declare-const x U)";

    /// <summary>What the solver says when the value of that constant is said back to it.</summary>
    private const string DECLARED_SORT_WORDS = "line 12 column 18: unknown constant U!val!0";

    private const string ONE_AT_THE_LIMIT = "ok sat 1 limit";
    private const string ONE_AND_NO_OTHER = "ok sat 1 complete";
    private const string NONE = "ok unsat 0 complete";

    /// <summary>
    /// Enumerations over a sort the problem only declares that never have a model to rule out:
    /// the id, the problem, the most models, the constant projected on, the row in a few words,
    /// and the digest of the row a node gave for it before an enumeration could end at a value
    /// the solver does not take back. The rows are the same now, on every platform.
    /// </summary>
    private static readonly object[] ENDED_WELL =
    [
        // One model asked for: nothing is said back.
        new object[] { "u-max-1", "(declare-sort U 0)(declare-const x U)", 1, "x", ONE_AT_THE_LIMIT, "6d334d577f9b34f40fc52637b6f6feab7eaf51978c31465d4de559e016d089ad" },
        new object[] { "u-max-1-two-constants", "(declare-sort U 0)(declare-const x U)(declare-const y U)(assert (distinct x y))", 1, "x", ONE_AT_THE_LIMIT, "b97b2620aee7f520a9d184c1156b5cbf223015561072ddbb9447461465c4142d" },
        new object[] { "u-field-max-1", "(declare-sort U 0)(declare-datatype D ((mk (u U) (n Int))))(declare-const d D)", 1, "d", ONE_AT_THE_LIMIT, "c298815bb1a000ff461c47e48c1783ae04c61cc7d27d3571ca051cdd8e17aa30" },
        new object[] { "u-array-max-1", "(declare-sort U 0)(declare-const a (Array U U))(declare-const u U)(assert (not (= (select a u) u)))", 1, "a", ONE_AT_THE_LIMIT, "b61fed5e112dc1bf5658613be15b431982fff77155b6dfaa7a97ab40215c864c" },

        // Values that hold no element of the sort: they are read back, and there is no other.
        new object[] { "u-option-none-only", "(declare-sort U 0)(declare-datatype Opt (par (T) ((none) (some (get T)))))(declare-const o (Opt U))(assert (= o (as none (Opt U))))", 3, "o", ONE_AND_NO_OTHER, "2ec74ed5775537f062241bed183637a85c90daabd133faae46e5fc3a0ad8e196" },
        new object[] { "u-array-index-constant", "(declare-sort U 0)(declare-const a (Array U Int))(assert (= a ((as const (Array U Int)) 7)))", 3, "a", ONE_AND_NO_OTHER, "4ed1a4f840fb10a19b79b19f4b7b1cc022571820981bc4ba5438043aaa3d455b" },
        new object[] { "u-seq-empty", "(declare-sort U 0)(declare-const q (Seq U))(assert (= (seq.len q) 0))", 3, "q", ONE_AND_NO_OTHER, "1c8a723ccf176e0611ae00483aa862aaa00e9e47d7f775081f9b31248fe0e324" },

        // No model at all.
        new object[] { "u-unsat", "(declare-sort U 0)(declare-const x U)(declare-const y U)(assert (distinct x y))(assert (= x y))", 3, "x", NONE, "8218180a8d5f74b55ec8bc5888753fed627416930668a0f36bd0069ca4556d5e" }
    ];

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Sort Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.ReadBack))]
    public void AnEnumerationWhoseValuesAreReadBackGoesAsFarAsItIsAskedTest(string id)
    {
        var one = SmtProjectionCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem(), new SmtOptionsData());

        TestContext.Out.WriteLine($"{one.Id}: {string.Join(" | ", solved.Values.Select(OnOneLine))}");
        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo(one.Words), solved.ToString());
            Assert.That(solved.Values, Is.Unique, "no model is found twice: each was ruled out before the next check");
            Assert.That(solved.Values, Has.Count.EqualTo(solved.Count));
            Assert.That(solved.Message, Is.Empty);
        });
    }

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.NotReadBack))]
    public void AnEnumerationOverValuesTheSolverNamesItselfEndsWithTheModelsItFoundTest(string id)
    {
        // Every shape of a sort whose values hold an element of a sort the problem only declares:
        // the enumeration finds models until one holds such an element, and ends there.
        var one = SmtProjectionCases.Of(id);
        var problem = one.Problem();

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());
        var row = SmtNodeRig.Row(problem, new SmtOptionsData());

        TestContext.Out.WriteLine($"{one.Id}: {solved.Words()} | {solved.ReasonText} | {string.Join(" | ", solved.Values.Select(OnOneLine))}");
        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo(one.Words), solved.ToString());
            Assert.That(solved.Status, Is.EqualTo(SmtResultWords.OK));
            Assert.That(solved.Verdict, Is.EqualTo(SmtResultWords.SAT));
            Assert.That(solved.Reason, Is.EqualTo(SmtResultWords.OTHER));
            Assert.That(solved.Message, Is.Empty, "the problem is well formed: this is no error of its author's");
            Assert.That(solved.Complete, Is.False, "that there is no other model is not known");
            Assert.That(solved.LimitReached, Is.False);
            Assert.That(solved.Count, Is.GreaterThanOrEqualTo(1), "the models found are kept");
            Assert.That(solved.Models, Has.Count.EqualTo(solved.Count));
            Assert.That(solved.Values, Has.Count.EqualTo(solved.Count));
            Assert.That(solved.Values, Is.Unique);
            Assert.That(solved.Checks, Is.EqualTo(solved.Count), "no check after the assertion the solver did not take");
            Assert.That(solved.ReasonText, Does.Contain("unknown constant"), "the solver's own answer to the assertion is kept as its word");
            Assert.That(solved.ReasonText, Does.Contain($"{one.Declared}!val!"), "and it names the element the solver made up");

            if (solved.Values.Count > 0)
                Assert.That(solved.Values[^1], Does.Contain($"{one.Declared}!val!"), "the value that was not read back is the last one found");

            Assert.That(SmtNodeRig.Words(row), Is.EqualTo(solved.Words()));
            Assert.That(SmtRowCheckRig.Holds(row, problem, new SmtOptionsData()), Is.True, "a row a session of this controller writes is a row the host takes");
        });
    }

    #endregion

    #region Declared Sort Tests

    [Test]
    public void ASecondModelOverADeclaredSortCannotBeAskedForAndTheFirstIsKeptTest()
    {
        // The row that ended as an error of the problem's input: a constant of a sort the problem
        // only declares, two models asked for. The first model is found; its value is the
        // solver's own name for an element, which it does not take when the model is ruled out.
        var problem = DeclaredSort();

        var solved = SmtSolverRig.Solve(problem, new SmtOptionsData());
        var row = SmtNodeRig.Row(problem, new SmtOptionsData());
        var (validation, _, _) = SmtHostChecks.Check(problem, row);

        TestContext.Progress.WriteLine($"the row of the constant of a declared sort: {SmtNodeRig.Platform} {row.Digest} fuel {row.Fuel} reason text '{row.ReasonText}'");
        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat other 1 incomplete"), solved.ToString());
            Assert.That(solved.Message, Is.Empty);
            Assert.That(solved.Complete, Is.False);
            Assert.That(solved.LimitReached, Is.False);
            Assert.That(solved.Models, Has.Count.EqualTo(1));
            Assert.That(solved.Values, Has.Count.EqualTo(1));
            Assert.That(OnOneLine(solved.Values[0]), Is.EqualTo("((x U!val!0))"));
            Assert.That(solved.Checks, Is.EqualTo(1));
            Assert.That(solved.ReasonText, Is.EqualTo(DECLARED_SORT_WORDS), "the solver's own answer, the same words on every platform");

            Assert.That(SmtNodeRig.Words(row), Is.EqualTo("ok sat other 1 incomplete"));
            Assert.That(row.Reason, Is.EqualTo(SmtResultWords.OTHER));
            Assert.That(row.ReasonText, Is.EqualTo(DECLARED_SORT_WORDS));
            Assert.That(row.Count, Is.EqualTo(1));
            Assert.That(SmtResultDigest.Holds(row), Is.True);
            Assert.That(SmtRowCheckRig.Holds(row, problem, new SmtOptionsData()), Is.True, "the host takes the row");
            Assert.That(validation, Is.EqualTo(SmtRunWords.VALIDATED), "and its evaluator finds the model to be one");
        });
    }

    [TestCaseSource(nameof(ENDED_WELL))]
    public void ARowOverADeclaredSortThatNeverHadAModelToRuleOutEndsAsItAlwaysDidTest(string id, string text, int maxModels, string project, string words, string digest)
    {
        var problem = SmtSolverProblems.Enumerate(id, text, maxModels, project);

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Words(row), Is.EqualTo(words));
            Assert.That(row.Message, Is.Empty);
            Assert.That(row.ReasonText, Is.Empty);
            Assert.That(row.Digest, Is.EqualTo(digest), "the row a node gave for it before, to the digest");
            Assert.That(SmtRowCheckRig.Holds(row, problem, new SmtOptionsData()), Is.True);
        });
    }

    #endregion

    #region Identity Array Tests

    [Test]
    public void AValueTheSolverDoesNotReadBackEndsTheEnumerationWithItsModelsTest()
    {
        // The four arrays from one bit to one bit, under a logic without quantifiers. The solver
        // prints three of them as constant arrays and stores, and the fourth, the identity, as a
        // function; the logic refuses the function when it is said back.
        var solved = SmtSolverRig.Solve(IdentityArrays(), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo(NOT_READ_BACK), solved.ToString());
            Assert.That(solved.Status, Is.EqualTo("ok"), "the problem is well formed: this is no error of its author's");
            Assert.That(solved.Message, Is.Empty);
            Assert.That(solved.Complete, Is.False, "four models are known; that there is no fifth is not");
            Assert.That(solved.LimitReached, Is.False);
            Assert.That(solved.ReasonText, Does.EndWith("logic does not support quantifiers"), "the solver's own answer to the assertion is kept as its word");
            Assert.That(solved.Models, Has.Count.EqualTo(4));
            Assert.That(solved.Values, Is.Unique);
            Assert.That(solved.Values[^1], Does.Contain("(lambda ((x!1 (_ BitVec 1))) x!1)"), "the value that was not read back is the last one found");
            Assert.That(solved.Checks, Is.EqualTo(4), "no check after the assertion the solver did not take");

            foreach (var (model, values) in solved.Models.Zip(solved.Values))
                Assert.That(SmtSolverProblems.Validate(IDENTITY_ARRAYS, model, values).Class, Is.Not.EqualTo(SmtValidationClass.Failed), values);
        });
    }

    [Test]
    public void TheHostTakesTheRowOfAnEnumerationThatEndedSoTest()
    {
        var problem = IdentityArrays();

        var row = SmtNodeRig.Row(problem, new SmtOptionsData());

        TestContext.Progress.WriteLine($"the row of the arrays whose identity is not read back: {SmtNodeRig.Platform} {row.Digest} fuel {row.Fuel}");
        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Words(row), Is.EqualTo(NOT_READ_BACK));
            Assert.That(row.Reason, Is.EqualTo(SmtResultWords.OTHER));
            Assert.That(row.ReasonText, Does.EndWith("logic does not support quantifiers"));
            Assert.That(row.Count, Is.EqualTo(4));
            Assert.That(SmtResultDigest.Holds(row), Is.True);
            Assert.That(SmtRowCheckRig.Holds(row, problem, new SmtOptionsData()), Is.True, "a row a session of this controller writes is a row the host takes");
            Assert.That(row.Digest, Is.EqualTo(IDENTITY_ARRAYS_ROW), "and the same row on every platform");
        });
    }

    [Test]
    public void TheSameArraysEnumerateToTheirEndWhereTheLogicTakesAFunctionTest()
    {
        // Without a logic the function is read back, and the enumeration says that there are four.
        var solved = SmtSolverRig.Solve(SmtSolverProblems.Enumerate("arrays", "(declare-const a (Array (_ BitVec 1) (_ BitVec 1)))", 8, "a"), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo("ok sat 4 complete"), solved.ToString());
            Assert.That(solved.Values, Has.Some.Contains("lambda"));
        });
    }

    #endregion

    #region Tools

    private static SmtProblemData IdentityArrays()
    {
        return SmtSolverProblems.Enumerate("identity-arrays", IDENTITY_ARRAYS, 8, "a");
    }

    private static SmtProblemData DeclaredSort()
    {
        return SmtSolverProblems.Enumerate("u-max-2", DECLARED_SORT, 2, "x");
    }

    private static string OnOneLine(string values)
    {
        return string.Join(' ', values.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    #endregion
}
