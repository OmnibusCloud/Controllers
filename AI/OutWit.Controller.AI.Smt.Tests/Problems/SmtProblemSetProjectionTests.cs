using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// The sort of a constant an enumerate row is projected on, as the reader of a problem set holds
/// it. An enumeration says a found model's values back to the solver to rule the model out, and
/// the solver takes no element of a sort the problem only declares (<c>declare-sort</c>) back: a
/// row projected on a constant whose values hold such an element is refused when the set is read,
/// by its code, naming the entry, the constant and the sort, so that a lab learns it from the
/// preflight and no node is sent the problem. Every other sort is taken, also one that names an
/// uninterpreted sort where no value goes (<see cref="SmtProjectionCases"/>).
/// </summary>
[TestFixture]
public sealed class SmtProblemSetProjectionTests
{
    #region Constants

    // The code as a lab reads it: a public word of the contract, written out here.
    private const string PROJECT_SORT_UNSUPPORTED = "project_sort_unsupported";

    private const string UNKNOWN_CONSTANT = "unknown_constant";

    private const string TWO_SORTS = "(declare-sort U 0)(declare-const n Int)(declare-const x U)(declare-const y U)";

    #endregion

    #region Refusal Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Refused))]
    public void ARowProjectedOnValuesThatHoldAnUninterpretedSortIsRefusedTest(string id)
    {
        var one = SmtProjectionCases.Of(id);

        var refusal = SmtProblemSets.Refuse(one.Row(), PROJECT_SORT_UNSUPPORTED);

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Reason, Is.EqualTo(Reason(one.Entry, one.Project[one.Entry], one.Uninterpreted!)));
            Assert.That(refusal.Row, Is.EqualTo(1), "the line of the set");
            Assert.That(refusal.File, Is.EqualTo(one.Id), "the row's id");
            Assert.That(refusal.Line, Is.Zero, "a refusal of the row itself, not of a line of the problem's text");
            Assert.That(refusal.Reason, Does.Not.Contain("declare-const"), "a refusal never carries the text");
        });
    }

    [Test]
    public void TheRefusalIsCountedByItsCodeAndTheRowsBesideItAreTakenTest()
    {
        var refused = SmtProjectionCases.Of("uninterpreted");
        var taken = SmtProjectionCases.Of("uninterpreted-for-an-unused-parameter");

        var (rows, summary) = SmtProblemSets.Read(string.Join("\n", taken.Row(), refused.Row(), SmtProjectionCases.Of("int").Row()));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.IsAccepted), Is.EqualTo(new[] { true, false, true }));
            Assert.That(summary.RefusalCodes, Is.EqualTo(new Dictionary<string, int> { [PROJECT_SORT_UNSUPPORTED] = 1 }));
            Assert.That(summary.Refusals.Select(refusal => (refusal.Row, refusal.File)), Is.EqualTo(new[] { (2, refused.Id) }));
        });
    }

    [Test]
    public void TheFirstEntryThatBreaksARuleIsTheOneNamedTest()
    {
        static string Row(params string[] project)
        {
            return new SmtProjectionCase("a", TWO_SORTS, 2, project, "").Row();
        }

        Assert.Multiple(() =>
        {
            // The sort of an entry is looked at after its name, and an entry after the entries before it.
            Assert.That(SmtProblemSets.Refuse(Row("x", "z"), PROJECT_SORT_UNSUPPORTED).Reason, Does.StartWith("'project[0]' names 'x', whose values"));
            Assert.That(SmtProblemSets.Refuse(Row("z", "x"), UNKNOWN_CONSTANT).Reason, Does.StartWith("'project[0]' names 'z'"));
            Assert.That(SmtProblemSets.Refuse(Row("n", "y", "x"), PROJECT_SORT_UNSUPPORTED).Reason, Does.StartWith("'project[1]' names 'y', whose values"));
            Assert.That(SmtProblemSets.Refuse(Row("n", "n", "x"), "field_invalid").Reason, Does.StartWith("'project[1]' names 'n' again"));
        });
    }

    [Test]
    public void ALongNameOfAConstantOrOfASortIsCutInTheRefusalTest()
    {
        var sort = new string('S', 5_000);
        var constant = new string('c', 5_000);
        var row = new SmtProjectionCase("a", $"(declare-sort {sort} 0)(declare-const {constant} {sort})", 2, [constant], "").Row();

        var refusal = SmtProblemSets.Refuse(row, PROJECT_SORT_UNSUPPORTED);

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Reason, Is.EqualTo(Reason(0, new string('c', 61) + "...", new string('S', 61) + "...")));
            Assert.That(refusal.Reason, Has.Length.LessThan(SmtProblemSets.MAX_REASON_CHARACTERS));
        });
    }

    #endregion

    #region Taken Tests

    [TestCaseSource(typeof(SmtProjectionCases), nameof(SmtProjectionCases.Taken))]
    public void ARowProjectedOnAnyOtherSortIsTakenTest(string id)
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
    public void ACheckAndAProofOverAnUninterpretedSortAreTakenTest()
    {
        // Only an enumeration says values back. A check may ask for the value of such a constant,
        // and a proof may be about it.
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
        // solver's input error, as it did before the rule.
        Assert.Multiple(() =>
        {
            Assert.That(SmtProblemSets.Accept(new SmtProjectionCase("a", "(declare-const x U)(declare-sort U 0)", 2, ["x"], "").Row()).Project, Is.EqualTo(new[] { "x" }));
            Assert.That(SmtProblemSets.Accept(new SmtProjectionCase("a", "(declare-sort U 0)(declare-const x Int)(declare-const x U)", 2, ["x"], "").Row()).Project, Is.EqualTo(new[] { "x" }));
        });
    }

    #endregion

    #region Tools

    /// <summary>The reason of the refusal, word for word: the contract a lab reads.</summary>
    private static string Reason(int entry, string constant, string sort)
    {
        return $"'project[{entry}]' names '{constant}', whose values hold elements of the sort '{sort}'. The problem only declares that sort (declare-sort): "
               + "the solver names its elements by names of its own that it takes in no later command, so an enumeration cannot tell it which models it has found";
    }

    #endregion
}
