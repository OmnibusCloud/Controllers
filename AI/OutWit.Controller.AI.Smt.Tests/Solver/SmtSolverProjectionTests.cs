using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The sorts an enumeration can be projected on, walked through the real solver module
/// (<see cref="SmtProjectionCases"/>). An enumeration rules a found model out by saying the
/// projected constants' values back to the solver, which works exactly when the solver reads
/// what it printed.
/// <list type="bullet">
/// <item>Every projection that is taken enumerates: at least two models are found and told apart
/// wherever the problem has two, each case to the words written beside it.</item>
/// <item>A projection whose values hold an element of a sort the problem only declares is refused
/// before a solver starts, on a node as when the set is read.</item>
/// <item>Behind the rule: a value the solver prints in a form it does not read back under the
/// problem's logic ends the enumeration there. The models found are whole and true and are kept;
/// that there is no other is not known, and the row says so. The pinned solver has one such form:
/// an array that is the identity is printed as a function, <c>(lambda ((x!1 ...)) x!1)</c>, which
/// a quantifier-free logic does not take.</item>
/// </list>
/// Needs the module (<see cref="SmtSolverModule"/>).
/// </summary>
[TestFixture]
[Category("Solver")]
public sealed class SmtSolverProjectionTests
{
    #region Constants

    // The words as a lab reads them: public words of the contract, written out here.
    private const string PROJECT_SORT_UNSUPPORTED = "project_sort_unsupported";

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

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Taken))]
    public void AProjectionThatIsTakenEnumeratesTest(string id)
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

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Refused))]
    public void AProjectionOnValuesThatHoldAnUninterpretedSortRunsNoSolverTest(string id)
    {
        var one = SmtProjectionCases.Of(id);

        var solved = SmtSolverRig.Solve(one.Problem(), new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(solved.Words(), Is.EqualTo($"refused {PROJECT_SORT_UNSUPPORTED}"), solved.ToString());
            Assert.That(solved.Message, Does.StartWith($"'project[{one.Entry}]' names '{one.Project[one.Entry]}', whose values hold elements of the sort '{one.Uninterpreted}'. "));
            Assert.That(solved.Fuel, Is.Zero, "no session ran");
            Assert.That(solved.Models, Is.Empty);
        });
    }

    #endregion

    #region Second Line Tests

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

    private static string OnOneLine(string values)
    {
        return string.Join(' ', values.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    #endregion
}
