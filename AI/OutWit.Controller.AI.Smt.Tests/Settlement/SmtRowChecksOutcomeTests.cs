using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The other half of the host's gate of a node's row: every row a node of this controller can
/// make is taken. The rule the host holds a row to and the code that makes a session's outcome
/// are two pieces of code, and nothing but these tests keeps them one: every way a session ends
/// as a script (<see cref="SmtSessionCases"/>, which reaches every branch of the outcome's making
/// and every answer of every query's conversation), the rows of the solver's own test problems
/// solved by the real solver (the golden rows), and the rows of the slice of real problems. A
/// rule that refuses any of them refuses an honest node, and fails here first.
/// </summary>
[TestFixture]
public sealed class SmtRowChecksOutcomeTests
{
    #region Constants

    // The envelope the solver's tests solve the slice in: none of its problems comes near it.
    private const long SLICE_RESOURCE_LIMIT = 2_000_000_000;
    private const int SLICE_HARD_CAP_SECONDS = 100;
    private const int SLICE_MEMORY_MEGABYTES = 1_024;

    // The one word of the controller no case reaches: a fault of the host's own outside the
    // solver, which needs a sandbox that fails. Its row is made where the row of a node without
    // a sandbox is made, and differs from it by that word alone.
    private const string HOST_FAULT = "host_fault";

    #endregion

    #region Session Tests

    [TestCaseSource(typeof(SmtSessionCases), nameof(SmtSessionCases.Ids))]
    public void TheRowOfEveryWayASessionEndsIsTakenTest(string id)
    {
        var one = SmtSessionCases.Of(id);
        var row = SmtRowCheckRig.Row(one);

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Words(row), Is.EqualTo(one.Expected), "the case ends as its name says");
            Assert.That(SmtResultDigest.Holds(row), Is.True, "the node gave the row its digest");
            Assert.That(SmtRowCheckRig.Holds(row, one.Problem, one.Options), Is.True, Describe(row));
        });
    }

    [Test]
    public void TheCasesReachEveryWordARowSaysTest()
    {
        var rows = SmtSessionCases.All.Select(SmtRowCheckRig.Row).ToList();
        var kinds = rows.Select(row => (row.Query, row.Status)).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.Status).Distinct(), Is.EquivalentTo(SmtResultWords.STATUSES));
            Assert.That(rows.Select(row => row.Verdict).Distinct(), Is.EquivalentTo(SmtResultWords.VERDICTS));
            Assert.That(rows.Select(row => row.Reason).Distinct(), Is.EquivalentTo(SmtResultWords.REASONS));
            Assert.That(rows.Select(row => row.Proof).Distinct(), Is.EquivalentTo(SmtResultWords.PROOFS));
            Assert.That(rows.Select(row => row.Code).Where(code => code.Length > 0 && !SmtRefusalCodes.All.Contains(code)).Distinct(),
                Is.EquivalentTo(SmtRowCheckRig.SolveCodes().Where(code => code != HOST_FAULT)), "every code of the controller's own but the one that needs a sandbox that fails");

            // Every status a session of each query can end in (a refusal and a fault of the node need no session of a query of their own).
            foreach (var query in new[] { SmtQuery.Check, SmtQuery.Enumerate, SmtQuery.Prove })
            {
                foreach (var status in new[] { SmtResultWords.OK, SmtResultWords.OUTPUT_EXCEEDED, SmtResultWords.ENGINE_CRASH })
                    Assert.That(kinds, Does.Contain((query, status)), $"{query} {status}");
            }

            // An unfinished session that kept what it had: a model beside a status that claims nothing.
            Assert.That(rows.Count(row => row.Status != SmtResultWords.OK && row.Models.Count > 0), Is.GreaterThanOrEqualTo(8));
            Assert.That(rows.Where(row => row.Verdict != SmtResultWords.NONE).Select(row => row.Status).Distinct(),
                Is.EquivalentTo(new[] { SmtResultWords.OK, SmtResultWords.OUTPUT_EXCEEDED }), "the two statuses that claim a verdict");
        });
    }

    #endregion

    #region Solver Tests

    [Test]
    [Category("Solver")]
    public void TheRowOfEveryProblemOfTheSolversTestsIsTakenTest()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        Assert.Multiple(() =>
        {
            for (var index = 0; index < SmtSolverCases.All.Count; index++)
            {
                var one = SmtSolverCases.All[index];
                var problem = SmtGoldenPackages.Indexed(one.Problem, index);
                var row = SmtNodeRig.Row(problem, one.Options);

                Assert.That(SmtNodeRig.Words(row), Is.EqualTo(one.Expected), one.Id);
                Assert.That(SmtRowCheckRig.Holds(row, problem, one.Options), Is.True, $"{one.Id}: {Describe(row)}");
            }
        });
    }

    [Test]
    [Category("Slice")]
    public void TheRowOfEveryProblemOfTheSliceIsTakenTest()
    {
        SmtSolverModule.Require();
        var folder = SmtSlice.RequireFolder();
        var options = new SmtOptionsData { ResourceLimit = SLICE_RESOURCE_LIMIT, HardCapSeconds = SLICE_HARD_CAP_SECONDS, MemoryMegabytes = SLICE_MEMORY_MEGABYTES };

        SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        var taken = 0;
        var refused = new List<string>();
        foreach (var listed in SmtSlice.Listing())
        {
            var text = SmtSlice.Read(folder, listed.Id, listed.Sha256);
            if (text == null)
            {
                if (SmtSolverModule.OnBuildServer)
                    Assert.Fail($"{listed.Id} is not in {folder}: the slice was not fetched whole");

                continue;
            }

            var sanitized = SmtSanitizer.Sanitize(text).Problem;
            Assert.That(sanitized, Is.Not.Null, $"{listed.Id} is refused by the sanitizer");

            var problem = new SmtProblemData { Id = listed.Id, Query = SmtQuery.Check, Text = sanitized!.Canonical, Hash = sanitized.Hash, Expected = listed.Status };
            var row = SmtNodeRig.Row(problem, options);
            if (SmtRowCheckRig.Holds(row, problem, options))
                taken++;
            else
                refused.Add($"{listed.Id} ({listed.Logic}): {Describe(row)}");
        }

        TestContext.Out.WriteLine($"{taken} rows of the slice taken, {refused.Count} refused");
        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.Empty, "a row of an honest node the host refuses");
            Assert.That(taken, Is.GreaterThan(0));
            if (SmtSolverModule.OnBuildServer)
                Assert.That(taken, Is.EqualTo(SmtSlice.PROBLEMS), "the whole slice");
        });
    }

    #endregion

    #region Tools

    private static string Describe(SmtResultData row)
    {
        return $"{row.Status} '{row.Code}' verdict {row.Verdict} reason {row.Reason} '{row.ReasonText}' proof {row.Proof} count {row.Count} complete {row.Complete} limit {row.LimitReached} "
               + $"models {row.Models.Count} values {row.Values.Count} checks {row.Checks} resource {row.ResourceCount} fuel {row.Fuel} peak {row.PeakMemoryBytes} transcript {row.Transcript.Length}";
    }

    #endregion
}
