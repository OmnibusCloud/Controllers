using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using static OutWit.Controller.AI.Smt.Tests.Mock.SmtSettledReader;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The rules of a dispute the other tests do not reach: more results under a ticket than the
/// round asked for are a dispute's cause in the first fan-out and in the repair round, and are
/// all dropped in the dispute round; the answers a dispute round's other packages are short of
/// may be one machine's second (<c>SmtRulings.Borrowable</c>), which leaves a leading digest
/// without two machines (<c>surplus_copies</c>) unless its answers are bound. Every document is
/// held to its schema.
/// </summary>
[TestFixture]
public sealed class SmtRulingsTests
{
    #region Constants

    private const string X_ABOVE_ONE = "(declare-const x Int)(assert (> x 1))";
    private const string X_TWO = "((define-fun x () Int 2))";
    private const string X_THREE = "((define-fun x () Int 3))";

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

    #region Surplus Tests

    [TestCase(true)]
    [TestCase(false)]
    public void ThreeResultsUnderATicketOfTheFirstFanOutAreADisputeOfSurplusTest(bool bound)
    {
        // Three answers of one digest where two were asked for: which two are the copies cannot
        // be told, so none is taken as agreement, and three more machines settle the problem.
        m_rig.Binds = bound;
        var (set, options, packages) = Packages(1, 1);
        var returned = Copies(packages[0], 3, X_TWO);

        var disputes = m_rig.Disputes(packages, returned, [], []);
        var settled = Copies(disputes[0], 3, X_TWO);
        var run = Settle(set, options, packages, returned, [], [], disputes, settled, 1);
        var dispute = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(disputes.Sum(package => package.ProblemCount), Is.EqualTo(1), "the problem goes to three more machines");
            Assert.That(dispute.GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.SURPLUS));
            Assert.That(dispute.GetProperty("first_copies").GetInt32(), Is.EqualTo(3));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.RESOLVED));
            Assert.That(CountOf(run, "dispute_causes", SmtRunWords.SURPLUS), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(Count(run, "rejected_results"), Is.Zero, "in the first fan-out a crowded ticket is kept, not refused");
        });
    }

    [Test]
    public void ThreeResultsUnderATicketOfTheRepairRoundAreADisputeOfSurplusTest()
    {
        // The first fan-out lost the problem (its node's fault code); the repair round, which asks
        // two machines, brought three answers back under its ticket.
        var (set, options, packages) = Packages(1, 0);
        var returned = new[] { SmtRunRig.Fault(m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), SmtNodeFaults.MEMORY_INSUFFICIENT) };

        var repairs = m_rig.Repairs(packages, returned, 0);
        var repaired = Copies(repairs[0], 3, X_TWO);
        var disputes = m_rig.Disputes(packages, returned, repairs, repaired);
        var settled = Copies(disputes[0], 3, X_TWO);
        var run = Settle(set, options, packages, returned, repairs, repaired, disputes, settled, 0);
        var dispute = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(package => package.ProblemCount), Is.EqualTo(1));
            Assert.That(disputes.Sum(package => package.ProblemCount), Is.EqualTo(1), "the repair round's surplus sends the problem to three more machines");
            Assert.That(dispute.GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.SURPLUS));
            Assert.That(dispute.GetProperty("first_copies").GetInt32(), Is.EqualTo(3));
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
        });
    }

    [Test]
    public void FourResultsUnderATicketOfTheDisputeRoundAreAllDroppedTest()
    {
        var (set, options, packages) = Packages(1, 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_THREE)) };

        var disputes = m_rig.Disputes(packages, returned, [], []);
        var settled = Copies(disputes[0], 4, X_TWO);
        var run = Settle(set, options, packages, returned, [], [], disputes, settled, 1);
        var dispute = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(CountOf(run, "rejected", "surplus"), Is.EqualTo(4), "which three are the answers cannot be told, so none is");
            Assert.That(dispute.GetProperty("copies").GetInt32(), Is.Zero);
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.TOO_FEW_COPIES));
            Assert.That(Row(run, 0).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED));
            Assert.That(Row(run, 0).GetProperty("code").GetString(), Is.EqualTo(SmtRunWords.TOO_FEW_COPIES));
        });
    }

    #endregion

    #region Borrowable Tests

    [TestCase(false, SmtRunWords.SURPLUS_COPIES)]
    [TestCase(true, SmtRunWords.RESOLVED)]
    public void BorrowableLeavesOutTheUnboundAnswersTheOtherPackagesAreShortOfTest(bool bound, string outcome)
    {
        // Two problems disputed, each in a dispute package of its own. The first package came back
        // three times with one digest, the second once: the second is two answers short, so two
        // of the first's three may be the second answers of nodes that were given both. Unbound,
        // the leading digest then rests on one machine; bound, its three answers came back for
        // three tasks of the first package, and none was refused.
        m_rig.Binds = bound;
        var (set, options, packages) = Packages(2, 1);
        var returned = packages.SelectMany(package => new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)) }).ToList();
        var disputes = SmtSettleRig.Read(set, options).Select(problem => m_rig.Publish([problem], options)).ToList();
        var settled = Copies(disputes[0], 3, X_TWO).Concat(Copies(disputes[1], 1, X_TWO)).ToList();

        var run = Settle(set, options, packages, returned, [], [], disputes, settled, 1);
        var records = Integrity(run).GetProperty("disputes").EnumerateArray().ToDictionary(record => record.GetProperty("index").GetInt32());

        Assert.Multiple(() =>
        {
            Assert.That(records[0].GetProperty("copies").GetInt32(), Is.EqualTo(3));
            Assert.That(records[0].GetProperty("agreeing").GetInt32(), Is.EqualTo(3));
            Assert.That(records[0].GetProperty("outcome").GetString(), Is.EqualTo(outcome));
            Assert.That(Row(run, 0).GetProperty("status").GetString(), Is.EqualTo(bound ? SmtResultWords.OK : SmtRunWords.DISPUTED));
            Assert.That(records[1].GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.TOO_FEW_COPIES), "one answer settles nothing");
        });
    }

    [Test]
    public void ARoundWithEveryPackageAnsweredThriceLendsNothingTest()
    {
        // Unbound answers, and no package of the round short of its three: none of them can be a
        // second answer of a node that robbed another package, so two against one is settled.
        m_rig.Binds = false;
        var (set, options, packages) = Packages(2, 1);
        var returned = packages.SelectMany(package => new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)) }).ToList();
        var disputes = SmtSettleRig.Read(set, options).Select(problem => m_rig.Publish([problem], options)).ToList();
        var settled = disputes.SelectMany(package => new[]
        {
            m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE))
        }).ToList();

        var run = Settle(set, options, packages, returned, [], [], disputes, settled, 1);

        Assert.Multiple(() =>
        {
            Assert.That(Integrity(run).GetProperty("disputes").EnumerateArray().Select(record => record.GetProperty("outcome").GetString()), Has.All.EqualTo(SmtRunWords.RESOLVED));
            Assert.That(Rows(run).Select(row => row.GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.DISPUTE));
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

    /// <summary>Copies of one model of a package's problems, each a result package of its own.</summary>
    private List<SmtResultPackageData> Copies(SmtTaskPackageData package, int count, string model)
    {
        return Enumerable.Range(0, count).Select(_ => m_rig.Answer(package, _ => SmtSettleRig.Sat(model))).ToList();
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired,
        IReadOnlyList<SmtTaskPackageData> disputes, IReadOnlyList<SmtResultPackageData> settled, double rate)
    {
        var run = m_rig.Settle(set, options, issued, returned, repairs, repaired, disputes, settled, rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    #endregion
}
