using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using static OutWit.Controller.AI.Smt.Tests.Mock.SmtSettledReader;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// A node refuses a problem only for what the host checked with the same code before it made the
/// package (<c>SmtSolvePlanPremiseTests</c>), so a node's <c>refused</c> for a problem the host
/// took is no answer: the problem goes out again under a reason of its own, and a problem refused
/// again has no result, with that reason. A refusal beside an honest answer of a package asked for
/// twice leaves the problem short of its copies. Every document is held to its schema.
/// </summary>
[TestFixture]
public sealed class SmtNodeRefusalTests
{
    #region Constants

    private const string X_ABOVE_ONE = "(declare-const x Int)(assert (> x 1))";
    private const string X_TWO = "((define-fun x () Int 2))";

    #endregion

    #region Fields

    private SmtSettleRig m_rig = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_rig = new SmtSettleRig();
    }

    [TearDown]
    public void TearDown()
    {
        m_rig.Dispose();
    }

    #endregion

    #region Refusal Tests

    [TestCase(SmtRefusalCodes.SYNTAX_ERROR)]
    [TestCase("hash_mismatch")]
    public void ANodesRefusalOfAProblemTheHostTookGoesOutAgainTest(string code)
    {
        var (set, options, packages) = Packages(1, 0);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Refusal(code)) };

        var repairs = m_rig.Repairs(packages, returned, 0);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 0);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1), "the refusal is no answer");
            Assert.That(CountOf(run, "repairs_by_reason", SmtRunWords.NODE_REFUSED), Is.EqualTo(1), "and the repair says why");
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtResultWords.OK));
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo(SmtResultWords.SAT));
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), "the repair pair answers it");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(2), "a refusal is not one of the answers it was ruled on");
            Assert.That(Count(run, "copies_made"), Is.EqualTo(2));
        });
    }

    [Test]
    public void AProblemRefusedAgainHasNoResultTest()
    {
        var (set, options, packages) = Packages(1, 0);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Refusal(SmtRefusalCodes.SYNTAX_ERROR)) };

        var repairs = m_rig.Repairs(packages, returned, 0);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Refusal(SmtRefusalCodes.SYNTAX_ERROR)), m_rig.Answer(again, _ => SmtSettleRig.Refusal(SmtRefusalCodes.SYNTAX_ERROR)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 0);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.NO_RESULT), "nodes refused what the host took: no answer is given");
            Assert.That(row.GetProperty("code").GetString(), Is.EqualTo(SmtRunWords.NODE_REFUSED));
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo(SmtResultWords.NONE));
            Assert.That(CountOf(run, "no_result_by_reason", SmtRunWords.NODE_REFUSED), Is.EqualTo(1));
            Assert.That(Count(run, "one_copy"), Is.Zero, "nothing is settled on a refusal");
        });
    }

    [Test]
    public void ARefusalBesideAnAnswerOfAPackageAskedForTwiceLeavesItShortTest()
    {
        var (set, options, packages) = Packages(1, 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[0], _ => SmtSettleRig.Refusal(SmtRefusalCodes.SYNTAX_ERROR)) };

        var repairs = m_rig.Repairs(packages, returned, 1);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 1);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(CountOf(run, "repairs_by_reason", SmtRunRig.CopiesShort), Is.EqualTo(1), "the problem has one answer");
            Assert.That(Count(run, "disputes"), Is.Zero, "a refusal is no copy to disagree with");
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(Word(run, "sampling"), Is.EqualTo("as_asked"));
        });
    }

    #endregion

    #region Tools

    /// <summary>A set of one satisfiable problem a row, read with options of a rate, each problem a package of its own.</summary>
    private (string Set, SmtOptionsData Options, List<SmtTaskPackageData> Packages) Packages(int count, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of(Enumerable.Range(0, count).Select(index => JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = $"p{index}", ["smt2"] = X_ABOVE_ONE })));
        var packages = SmtSettleRig.Read(set, options).Select(problem => m_rig.Publish([problem], options)).ToList();
        return (set, options, packages);
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired, double rate)
    {
        var run = m_rig.Settle(set, options, issued, returned, repairs, repaired, [], [], rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    #endregion
}
