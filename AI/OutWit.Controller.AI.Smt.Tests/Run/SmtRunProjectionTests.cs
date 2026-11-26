using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// An enumeration over values the solver does not take back, through the bundled scripts on the
/// in-process nodes: what a lab sees of it. No row is refused for the sort of its projected
/// constant, so the preflight takes every row. In a run, an enumeration projected on a constant
/// of a sort the problem only declares is answered on two machines with one digest: its row is
/// taken and settled as an answer with the one model it found, not complete, for the reason
/// <c>other</c>, and the host's evaluator finds that model to be one. So is the enumeration that
/// ends at an array the solver prints as a function (the identity array under a logic without
/// quantifiers), with its four models.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunProjectionTests : SmtRunTestBase
{
    #region Constants

    // The word as a lab reads it: a public word of the contract, written out here.
    private const string OTHER = "other";

    private const string DECLARED_SORT = "uninterpreted";
    private const string NO_VALUE_OF_IT = "uninterpreted-for-an-unused-parameter";
    private const string IDENTITY_ARRAYS = "identity-arrays";

    private const string IDENTITY_ARRAYS_PROBLEM = "(set-logic QF_ABV)(declare-const a (Array (_ BitVec 1) (_ BitVec 1)))";

    #endregion

    #region Tests

    [Test]
    public async Task ThePreflightTakesARowProjectedOnValuesTheSolverNamesItselfTest()
    {
        var job = Engine.Compile(Script("SmtPreflight.wit"));
        var status = await Engine.ScheduleAndWaitAsync(job, await UploadAsync(Set(), "problems.jsonl"), new SmtOptionsData());
        Assert.That(status.Result, Is.EqualTo(OutWit.Engine.Interfaces.WitProcessingResult.Completed), status.ToString());

        var preflight = (SmtPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(preflight.Runnable, Is.True);
            Assert.That(preflight.Rows, Is.EqualTo(3));
            Assert.That(preflight.Accepted, Is.EqualTo(3), "the sort of a projected constant is no ground for a refusal");
            Assert.That(preflight.Refused, Is.Zero);
            Assert.That(preflight.RefusalCodes, Is.Empty);
            Assert.That(preflight.Refusals, Is.Empty);
        });
    }

    [Test]
    public async Task ARunSettlesTheEnumerationsTheSolverCouldNotGoOnWithTest()
    {
        var run = await RunAsync(Set(), new SmtOptionsData { SamplingRate = 1 });
        var arrays = run.Row(0);
        var declared = run.Row(1);
        var whole = run.Row(2);

        Assert.Multiple(() =>
        {
            Assert.That(run.Rows.Select(row => row.GetProperty("id").GetString()), Is.EqualTo(new[] { IDENTITY_ARRAYS, DECLARED_SORT, NO_VALUE_OF_IT }));

            // The constant of a sort the problem only declares: the first model is found, and its
            // value is a name of the solver's own that it does not take back. An answer, with
            // that model.
            Assert.That(SmtRunChecks.Words(declared), Is.EqualTo("ok sat other 1 incomplete"));
            Assert.That(declared.GetProperty("reason").GetString(), Is.EqualTo(OTHER));
            Assert.That(declared.GetProperty("message").GetString(), Is.Empty);
            Assert.That(declared.GetProperty("count").GetInt32(), Is.EqualTo(1));
            Assert.That(declared.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(declared.GetProperty("limit_reached").GetBoolean(), Is.False);
            Assert.That(declared.GetProperty("models").GetArrayLength(), Is.EqualTo(1));
            Assert.That(declared.GetProperty("copies").GetInt32(), Is.EqualTo(2), "two machines answered");
            Assert.That(declared.GetProperty("agreed").GetBoolean(), Is.True, "with one digest");
            Assert.That(declared.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(declared.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "the host's evaluator finds the model to be one");

            // The enumeration that ended at an array printed as a function: an answer, with what it found.
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

            // And a projection that only names such a sort where no value goes enumerates to its end.
            Assert.That(SmtRunChecks.Words(whole), Is.EqualTo("ok sat 2 complete"));

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
            SmtProjectionCases.Of(DECLARED_SORT).Row(),
            SmtProjectionCases.Of(NO_VALUE_OF_IT).Row()
        ]);
    }

    #endregion
}
