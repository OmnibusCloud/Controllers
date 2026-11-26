using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// The sort of a projected constant through the bundled scripts, on the in-process nodes: what a
/// lab sees of it. A row projected on values the solver cannot be told back is refused when the
/// set is read: the preflight says so before anything is sent, and in a run the row is refused in
/// the results and no node is given its problem. An enumeration that ends where the solver does
/// not read a found model's value back (the identity array under a logic without quantifiers) is
/// answered on two machines with one digest: its row is taken and settled, with the models it
/// found, not complete, for the reason <c>other</c>.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunProjectionTests : SmtRunTestBase
{
    #region Constants

    // The words as a lab reads them: public words of the contract, written out here.
    private const string PROJECT_SORT_UNSUPPORTED = "project_sort_unsupported";
    private const string OTHER = "other";

    private const string REFUSED = "uninterpreted";
    private const string TAKEN = "uninterpreted-for-an-unused-parameter";
    private const string IDENTITY_ARRAYS = "identity-arrays";

    private const string IDENTITY_ARRAYS_PROBLEM = "(set-logic QF_ABV)(declare-const a (Array (_ BitVec 1) (_ BitVec 1)))";

    #endregion

    #region Tests

    [Test]
    public async Task ThePreflightSaysWhichRowIsProjectedOnValuesTheSolverCannotBeToldBackTest()
    {
        var job = Engine.Compile(Script("SmtPreflight.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(Set(), "problems.jsonl"), new SmtOptionsData());
        Assert.That(status.Result, Is.EqualTo(OutWit.Engine.Interfaces.WitProcessingResult.Completed), status.ToString());

        var preflight = (SmtPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.True);
            Assert.That(preflight.Rows, Is.EqualTo(3));
            Assert.That(preflight.Accepted, Is.EqualTo(2));
            Assert.That(preflight.Refused, Is.EqualTo(1));
            Assert.That(preflight.RefusalCodes.Select(count => (count.Name, count.Count)), Is.EqualTo(new[] { (PROJECT_SORT_UNSUPPORTED, 1) }));
            Assert.That(preflight.Refusals.Select(refusal => (refusal.Row, refusal.File, refusal.Code)), Is.EqualTo(new[] { (2, REFUSED, PROJECT_SORT_UNSUPPORTED) }), "the row, by its line and its id");
            Assert.That(preflight.Refusals.Select(refusal => refusal.Reason), Has.All.StartsWith("'project[0]' names 'x', whose values hold elements of the sort 'U'. "));
        });
    }

    [Test]
    public async Task ARunRefusesThatRowAndSettlesTheEnumerationTheSolverCouldNotGoOnWithTest()
    {
        var run = await RunAsync(Set(), new SmtOptionsData { SamplingRate = 1 });
        var arrays = run.Row(0);
        var refused = run.Row(1);
        var taken = run.Row(2);

        Assert.Multiple(() =>
        {
            Assert.That(run.Rows.Select(row => row.GetProperty("id").GetString()), Is.EqualTo(new[] { IDENTITY_ARRAYS, REFUSED, TAKEN }));

            // The row the set refused: no node was given its problem.
            Assert.That(refused.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.REFUSED));
            Assert.That(refused.GetProperty("code").GetString(), Is.EqualTo(PROJECT_SORT_UNSUPPORTED));
            Assert.That(refused.GetProperty("message").GetString(), Does.StartWith("'project[0]' names 'x', whose values hold elements of the sort 'U'. "));
            Assert.That(refused.GetProperty("copies").GetInt32(), Is.Zero);
            Assert.That(refused.TryGetProperty("digest", out _), Is.False, "no node answered it");

            // The enumeration that ended at a value the solver did not read back: an answer, with what it found.
            Assert.That(SmtRunChecks.Words(arrays), Is.EqualTo("ok sat other 4 incomplete"));
            Assert.That(arrays.GetProperty("reason").GetString(), Is.EqualTo(OTHER));
            Assert.That(arrays.GetProperty("message").GetString(), Is.Empty);
            Assert.That(arrays.GetProperty("count").GetInt32(), Is.EqualTo(4));
            Assert.That(arrays.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(arrays.GetProperty("limit_reached").GetBoolean(), Is.False);
            Assert.That(arrays.GetProperty("models").GetArrayLength(), Is.EqualTo(4));
            Assert.That(arrays.GetProperty("copies").GetInt32(), Is.EqualTo(2), "two machines answered");
            Assert.That(arrays.GetProperty("agreed").GetBoolean(), Is.True, "with one digest");
            Assert.That(arrays.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(arrays.GetProperty("validation").GetString(), Is.Not.EqualTo(SmtRunWords.FAILED));

            // And a projection that only names an uninterpreted sort where no value goes enumerates to its end.
            Assert.That(SmtRunChecks.Words(taken), Is.EqualTo("ok sat 2 complete"));

            Assert.That(run.IntegrityCount("disputes") + run.IntegrityCount("lies") + run.IntegrityCount("repairs") + run.IntegrityCount("rejected_results"), Is.Zero, "nothing to report: every row a node gave was taken");
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    #endregion

    #region Tools

    private static string Set()
    {
        return SmtSets.Of([
            new SmtProjectionCase(IDENTITY_ARRAYS, IDENTITY_ARRAYS_PROBLEM, 8, ["a"], "").Row(),
            SmtProjectionCases.Of(REFUSED).Row(),
            SmtProjectionCases.Of(TAKEN).Row()
        ]);
    }

    #endregion
}
