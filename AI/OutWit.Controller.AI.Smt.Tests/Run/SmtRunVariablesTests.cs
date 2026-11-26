using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// What a finished SmtRun leaves in its job's variables: a submitter can read any variable of a
/// finished job by its name, and the platform hands back the results of a fan-out in the order it
/// keeps them, one node's after another's, each with the ticket of its package and the id of its
/// blob. The documents say how many machines answered, never which; so the script empties the
/// variables of what the nodes returned once the documents are written. The packages the run gave
/// out (in the run's own order) and the documents stay.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunVariablesTests : SmtRunTestBase
{
    #region Constants

    private const int PROBLEMS = 40;

    /// <summary>The variables of SmtRun that hold what a fan-out returned.</summary>
    private static readonly string[] RETURNED = ["solved", "repaired", "settled"];

    #endregion

    #region Setup

    [SetUp]
    public void Reset()
    {
        Liars.Lie = null;
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
    }

    #endregion

    #region Variable Tests

    [Test]
    public async Task WhatTheNodesReturnedIsNotLeftInTheJobTest()
    {
        // Every node says it could not work on its packages: the repair round sends them again,
        // so the first fan-out and the repair round both returned a result for every package.
        Liars.Liars = SmtLiars.All;
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Fault(honest, SmtNodeFaults.SANDBOX_UNAVAILABLE));

        var run = await RunAsync(Set(), new SmtOptionsData { SamplingRate = 1 });

        Assert.That(run.FanOut("main").Packages, Is.GreaterThan(0), "the first fan-out ran");
        Assert.That(run.FanOut("repair").Packages, Is.GreaterThan(0), "and the repair round");
        AssertLeftOut(run);
    }

    [Test]
    public async Task WhatADisputeReturnedIsNotLeftInTheJobTest()
    {
        // A node whose every unsat row says unknown instead, its digest made anew: its copies
        // differ from the honest ones, and the dispute round settles them.
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Verdict == SmtResultWords.UNSAT))
            {
                row.Verdict = SmtResultWords.UNKNOWN;
                row.Reason = SmtResultWords.INCOMPLETE;
                row.Digest = SmtResultDigest.Of(row);
            }
        }));

        var run = await RunAsync(Set(), new SmtOptionsData { SamplingRate = 1 });

        Assert.That(run.FanOut("dispute").Packages, Is.GreaterThan(0), "the dispute round ran");
        AssertLeftOut(run);
    }

    #endregion

    #region Tools

    private static void AssertLeftOut(SmtRunDocuments run)
    {
        Assert.Multiple(() =>
        {
            foreach (var name in RETURNED)
                Assert.That(run.Job.Variables[name].Value as IEnumerable<SmtResultPackageData?>, Is.Not.Null.And.Empty, $"{name}: what the nodes returned is not left in the job");

            Assert.That(run.Job.Variables["packages"].Value as IEnumerable<SmtTaskPackageData>, Is.Not.Empty, "the packages the run gave out stay");
            Assert.That(run.Rows, Has.Count.EqualTo(PROBLEMS), "the documents are written");
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    private static string Set()
    {
        const string LIA = "(set-logic QF_LIA)(declare-const x Int)";

        return SmtSets.Of(Enumerable.Range(0, PROBLEMS).Select(index => index % 2 == 0
            ? $"{{\"id\":\"p{index}\",\"smt2\":\"{LIA}(assert (> x {index}))(assert (< x {index + 5}))\"}}"
            : $"{{\"id\":\"p{index}\",\"smt2\":\"{LIA}(assert (> x {index}))(assert (< x {index}))\"}}"));
    }

    #endregion
}
