using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtRun through the engine against a node that refuses every problem it is given, the cheap way
/// to leave problems unanswered for good when a refusal is taken as the problem's answer. A node
/// refuses only what the host checked before it made the package, so the host takes a refusal for
/// no answer: the problems go out again under <c>node_refused</c>, every row ends with the honest
/// answer, and no row says <c>refused</c>.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunNodeRefusalTests : SmtRunTestBase
{
    #region Constants

    private const int PROBLEMS = 48;

    #endregion

    #region Fields

    private string m_set = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_set = SmtSets.Of(Enumerable.Range(0, PROBLEMS).Select(Line));
    }

    [SetUp]
    public void Reset()
    {
        Liars.Lie = null;
        Liars.Liars = SmtLiars.First;
        Liars.Lies = 0;
    }

    #endregion

    #region Refusal Tests

    [TestCase(0.0)]
    [TestCase(1.0)]
    public async Task ANodeThatRefusesEveryProblemLeavesNoneUnansweredTest(double rate)
    {
        var honest = await RunAsync(m_set, new SmtOptionsData { SamplingRate = rate });

        Liars.Lie = answer => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, answer, rows =>
        {
            foreach (var row in rows)
                Refuse(row);
        }));

        var run = await RunAsync(m_set, new SmtOptionsData { SamplingRate = rate });
        var reason = rate > 0 ? SmtRunRig.CopiesShort : SmtRunWords.NODE_REFUSED;

        Assert.Multiple(() =>
        {
            Assert.That(Liars.Lies, Is.GreaterThan(0));
            Assert.That(run.Rows.Select(row => row.GetProperty("status").GetString()), Has.None.EqualTo(SmtResultWords.REFUSED), "no refusal is an answer");
            Assert.That(run.IntegrityCount("no_result"), Is.Zero, "every problem is answered by a machine that did not refuse it");
            Assert.That(run.IntegrityCount("repairs"), Is.GreaterThan(0));
            Assert.That(run.IntegrityCount("repairs_by_reason", reason), Is.EqualTo(run.IntegrityCount("repairs")), rate > 0 ? "a refused copy leaves its problem short" : "the refused problems go out again");
            foreach (var row in honest.Rows)
            {
                var index = row.GetProperty("index").GetInt32();
                Assert.That(run.Row(index).GetProperty("digest").GetString(), Is.EqualTo(row.GetProperty("digest").GetString()), $"problem {index}: the honest row");
            }
        });

        SmtRunChecks.AssertSchemas(run);
        SmtRunChecks.AssertNamesNoMachine(run);
    }

    #endregion

    #region Tools

    /// <summary>A satisfiable check and an unsatisfiable one, by turns.</summary>
    private static string Line(int index)
    {
        const string LIA = "(set-logic QF_LIA)(declare-const x Int)";

        var smt2 = index % 2 == 0
            ? $"{LIA}(assert (> x {index}))(assert (< x {index + 5}))"
            : $"{LIA}(assert (> x {index}))(assert (< x {index}))";

        return $"{{\"id\":\"n{index}\",\"query\":\"check\",\"smt2\":\"{smt2}\"}}";
    }

    /// <summary>A row a node writes when it refuses its problem before a session starts, its digest its own.</summary>
    private static void Refuse(SmtResultData row)
    {
        row.Status = SmtResultWords.REFUSED;
        row.Code = SmtRefusalCodes.SYNTAX_ERROR;
        row.Message = "the problem was refused";
        row.Verdict = SmtResultWords.NONE;
        row.Reason = SmtResultWords.NONE;
        row.ReasonText = "";
        row.Proof = SmtResultWords.NONE;
        row.Count = 0;
        row.Complete = false;
        row.LimitReached = false;
        row.Models = [];
        row.Values = [];
        row.Checks = 0;
        row.ResourceCount = 0;
        row.Fuel = 0;
        row.PeakMemoryBytes = 0;
        row.Transcript = "";
        row.Digest = SmtResultDigest.Of(row);
    }

    #endregion
}
