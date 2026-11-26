using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// The constants an enumerate row is projected on, as the reader of a problem set holds them: a
/// name is a constant the problem declares, named once, and its sort is no ground for a refusal.
/// A row projected on a constant of any sort is taken (<see cref="SmtProjectionCases"/>), also
/// one whose values the solver names by names of its own (a sort the problem only declares): what
/// becomes of such an enumeration is the node's to say, with the models it found, and the rows
/// over such a sort that end well (one model asked for, a problem with no model) are rows like
/// any other.
/// </summary>
[TestFixture]
public sealed class SmtProblemSetProjectionTests
{
    #region Constants

    // The codes as a lab reads them: public words of the contract, written out here.
    private const string UNKNOWN_CONSTANT = "unknown_constant";
    private const string FIELD_INVALID = "field_invalid";

    private const string TWO_SORTS = "(declare-sort U 0)(declare-const n Int)(declare-const x U)(declare-const y U)";

    #endregion

    #region Taken Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Ids))]
    public void ARowProjectedOnAConstantOfAnySortIsTakenTest(string id)
    {
        var one = SmtProjectionCases.Of(id);

        var data = SmtProblemSets.Accept(one.Row());

        Assert.Multiple(() =>
        {
            Assert.That(data.Query, Is.EqualTo(SmtQuery.Enumerate));
            Assert.That(data.Project, Is.EqualTo(one.Project));
            Assert.That(data.MaxModels, Is.EqualTo(one.MaxModels));
        });
    }

    [Test]
    public void ASetWithRowsOverADeclaredSortHasNoRefusalTest()
    {
        var (rows, summary) = SmtProblemSets.Read(string.Join("\n",
            SmtProjectionCases.Of("uninterpreted-for-an-unused-parameter").Row(),
            SmtProjectionCases.Of("uninterpreted").Row(),
            SmtProjectionCases.Of("uninterpreted-field").Row(),
            SmtProjectionCases.Of("int").Row()));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.IsAccepted), Has.All.True);
            Assert.That(summary.RefusalCodes, Is.Empty);
            Assert.That(summary.Refusals, Is.Empty);
        });
    }

    [Test]
    public void ACheckAndAProofOverADeclaredSortAreTakenTest()
    {
        // A check may ask for the value of such a constant, and a proof may be about it.
        const string PROBLEM = "(declare-sort U 0)(declare-const x U)(declare-const y U)(assert (distinct x y))";

        Assert.Multiple(() =>
        {
            Assert.That(SmtProblemSets.Accept($"{{\"id\":\"a\",\"smt2\":\"{PROBLEM}\",\"values\":[\"x\"]}}").Values, Is.EqualTo(new[] { "x" }));
            Assert.That(SmtProblemSets.Accept($"{{\"id\":\"a\",\"query\":\"prove\",\"smt2\":\"{PROBLEM}\",\"goal\":\"(not (= x y))\"}}").Goal, Is.EqualTo("(not (= x y))"));
        });
    }

    [Test]
    public void ASortTheSolverWillRefuseItselfIsLeftToTheSolverTest()
    {
        // A sort that is used before it is declared, and a constant declared twice with two
        // sorts, are errors the solver reports: the row is taken, and its problem ends as the
        // solver's input error.
        Assert.Multiple(() =>
        {
            Assert.That(SmtProblemSets.Accept(new SmtProjectionCase("a", "(declare-const x U)(declare-sort U 0)", 2, ["x"], "").Row()).Project, Is.EqualTo(new[] { "x" }));
            Assert.That(SmtProblemSets.Accept(new SmtProjectionCase("a", "(declare-sort U 0)(declare-const x Int)(declare-const x U)", 2, ["x"], "").Row()).Project, Is.EqualTo(new[] { "x" }));
        });
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void TheFirstEntryThatBreaksARuleIsTheOneNamedTest()
    {
        static string Row(params string[] project)
        {
            return new SmtProjectionCase("a", TWO_SORTS, 2, project, "").Row();
        }

        Assert.Multiple(() =>
        {
            // An entry is looked at after the entries before it, whatever the sorts of their constants.
            Assert.That(SmtProblemSets.Refuse(Row("x", "z"), UNKNOWN_CONSTANT).Reason, Is.EqualTo("'project[1]' names 'z', which the problem does not declare as a constant"));
            Assert.That(SmtProblemSets.Refuse(Row("z", "x", "x"), UNKNOWN_CONSTANT).Reason, Does.StartWith("'project[0]' names 'z'"));
            Assert.That(SmtProblemSets.Refuse(Row("n", "y", "y"), FIELD_INVALID).Reason, Is.EqualTo("'project[2]' names 'y' again: a constant is named once"));
            Assert.That(SmtProblemSets.Refuse(Row("n", "n", "z"), FIELD_INVALID).Reason, Does.StartWith("'project[1]' names 'n' again"));
        });
    }

    #endregion
}
