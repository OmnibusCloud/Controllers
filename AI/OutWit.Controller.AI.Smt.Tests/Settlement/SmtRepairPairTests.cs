using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The repair round restores the copies the sampling asked for, on rows made up as a node writes
/// them, without nodes and without the solver. A problem of a package the first fan-out asked for
/// twice that came back with fewer than two answers (a copy a node refused with its fault code, a
/// copy whose result package was refused, a row of a fault of the node) goes out again beside the
/// problems without an answer, and the repair round asks two machines for each of its packages.
/// Two answers count as two machines only when they come from one fan-out: the lone answer of the
/// first fan-out and a lone answer of the repair round may be one machine's. The lone answer is
/// still compared with the repair round's answers by digest. Every document is held to its schema.
/// </summary>
[TestFixture]
public sealed class SmtRepairPairTests
{
    #region Constants

    private const string X_ABOVE_ONE = "(declare-const x Int)(assert (> x 1))";
    private const string X_ABOVE_X = "(declare-const x Int)(assert (> x x))";
    private const string X_TWO = "((define-fun x () Int 2))";
    private const string X_THREE = "((define-fun x () Int 3))";

    private const string COPIES_SHORT = "copies_short";

    private const string FAULT_CODE = "fault_code";
    private const string REFUSED_RESULT = "refused_result";
    private const string NODE_FAULT_ROW = "node_fault_row";

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

    #region Repair Tests

    [TestCase(FAULT_CODE)]
    [TestCase(REFUSED_RESULT)]
    [TestCase(NODE_FAULT_ROW)]
    public void ASampledProblemWithOneAnswerGoesOutAgainTest(string loss)
    {
        // Both packages were asked for twice. The first came back with one answer and one copy
        // lost; the second with both: only the first one's problem goes out again.
        var (_, _, packages) = Packages(2, X_ABOVE_ONE, rate: 1);
        var returned = new[]
        {
            m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), Lost(packages[0], loss),
            m_rig.Answer(packages[1], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[1], _ => SmtSettleRig.Sat(X_TWO))
        };

        var repairs = m_rig.Repairs(packages, returned, rate: 1);

        Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1), "the problem that lost one of the two copies its package was asked for, and no other");
    }

    [Test]
    public void TheRepairPairSettlesItAndTheSamplingIsAsAskedTest()
    {
        var (set, options, packages) = Packages(1, X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), Lost(packages[0], FAULT_CODE) };

        var repairs = m_rig.Repairs(packages, returned, rate: 1);
        var repaired = repairs.SelectMany(again => Copies(again, 2, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var disputes = m_rig.Disputes(packages, returned, repairs, repaired);
        var run = Settle(set, options, packages, returned, repairs, repaired, disputes, [], rate: 1);
        var row = Row(run, 0);
        var repair = FanOut(run, "repair");

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(disputes, Is.Empty, "the lone answer and the pair give one digest");
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("sat"));
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), "the repair pair is two machines");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(3), "the lone answer and the pair");
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));

            Assert.That(Count(run, "repairs"), Is.EqualTo(1));
            Assert.That(CountOf(run, "repairs_by_reason", COPIES_SHORT), Is.EqualTo(1), "the reason of the new case");
            Assert.That(CountOf(run, "node_faults_by_code", SmtNodeFaults.MEMORY_INSUFFICIENT), Is.EqualTo(1));
            Assert.That(Count(run, "one_copy"), Is.Zero);
            Assert.That(Count(run, "compared"), Is.EqualTo(1));
            Assert.That(Count(run, "agreements"), Is.EqualTo(1));
            Assert.That(Report(run).GetProperty("copies").GetProperty("two_or_more").GetInt32(), Is.EqualTo(1));

            Assert.That(FanOut(run, "main").GetProperty("complete").GetInt32(), Is.Zero, "the first fan-out came back short");
            Assert.That(repair.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(repair.GetProperty("selected").GetInt32(), Is.EqualTo(1), "every package of the repair round is asked for twice");
            Assert.That(repair.GetProperty("complete").GetInt32(), Is.EqualTo(1));
            Assert.That(Sampling(run), Is.EqualTo("as_asked"), "the repair round made up for it");
        });
    }

    [Test]
    public void ARepairPairThatLosesACopyAgainLeavesTheSamplingShortTest()
    {
        var (set, options, packages) = Packages(1, X_ABOVE_X, rate: 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Unsat()), Lost(packages[0], FAULT_CODE) };

        var repairs = m_rig.Repairs(packages, returned, rate: 1);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Unsat()), Lost(again, FAULT_CODE) }).ToList();
        var disputes = m_rig.Disputes(packages, returned, repairs, repaired);
        var run = Settle(set, options, packages, returned, repairs, repaired, disputes, [], rate: 1);
        var row = Row(run, 0);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(disputes, Is.Empty, "the two answers give one digest");
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("unsat"));
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(2), "an answer of each fan-out");
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY), "two fan-outs may have placed both answers on one machine");

            Assert.That(Count(run, "one_copy"), Is.EqualTo(1));
            Assert.That(Count(run, "unsat_one_copy"), Is.EqualTo(1));
            Assert.That(Report(run).GetProperty("unsat_one_copy").GetProperty("indices").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(new[] { 0 }),
                "the report lists it among the unsat answers of one machine");
            Assert.That(Report(run).GetProperty("copies").GetProperty("two_or_more").GetInt32(), Is.Zero, "it is not counted as answered by two machines");
            Assert.That(FanOut(run, "repair").GetProperty("complete").GetInt32(), Is.Zero);
            Assert.That(Sampling(run), Is.EqualTo("short"));
        });
    }

    [Test]
    public void TheLoneAnswerThatDiffersFromTheRepairPairIsADisputeTest()
    {
        var (set, options, packages) = Packages(1, X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_THREE)), Lost(packages[0], FAULT_CODE) };

        var repairs = m_rig.Repairs(packages, returned, rate: 1);
        var repaired = repairs.SelectMany(again => Copies(again, 2, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var disputes = m_rig.Disputes(packages, returned, repairs, repaired);
        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "the lone answer and the pair differ: the problem goes to three more machines");

        var settled = disputes.SelectMany(dispute => Copies(dispute, 3, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, disputes, settled, rate: 1);
        var row = Row(run, 0);
        var record = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(row.GetProperty("model").GetString(), Is.EqualTo(X_TWO), "the digest the most of the three gave");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(6), "the lone answer, the pair and the three");
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.False);
            Assert.That(record.GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.DIGESTS_DIFFER));
            Assert.That(record.GetProperty("first_copies").GetInt32(), Is.EqualTo(3), "the lone answer and the pair");
            Assert.That(record.GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.RESOLVED));
            Assert.That(Count(run, "disagreements"), Is.EqualTo(1));
            Assert.That(Count(run, "lies"), Is.EqualTo(1), "the lone answer differs from the settled answer of the others");
            Assert.That(Count(run, "problems_with_lies"), Is.EqualTo(1));
            Assert.That(Sampling(run), Is.EqualTo("as_asked"), "the repair pair gave two answers in one fan-out");
        });
    }

    [Test]
    public void AnUnsampledProblemWithoutAnAnswerGetsTwoCopiesTest()
    {
        var (set, options, packages) = Packages(1, X_ABOVE_ONE, rate: 0);
        var returned = new[] { SmtRunRig.Fault(m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), SmtNodeFaults.SANDBOX_UNAVAILABLE) };

        var repairs = m_rig.Repairs(packages, returned, rate: 0);
        var repaired = repairs.SelectMany(again => Copies(again, 2, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var disputes = m_rig.Disputes(packages, returned, repairs, repaired);
        var run = Settle(set, options, packages, returned, repairs, repaired, disputes, [], rate: 0);
        var row = Row(run, 0);
        var repair = FanOut(run, "repair");

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(disputes, Is.Empty);
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtResultWords.OK));
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), "the repair round asked two machines");
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(CountOf(run, "rejected", "surplus"), Is.Zero, "the second answer is no surplus");
            Assert.That(CountOf(run, "repairs_by_reason", SmtNodeFaults.SANDBOX_UNAVAILABLE), Is.EqualTo(1));
            Assert.That(repair.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(repair.GetProperty("selected").GetInt32(), Is.EqualTo(1));
            Assert.That(repair.GetProperty("complete").GetInt32(), Is.EqualTo(1));
            Assert.That(Sampling(run), Is.EqualTo("none"), "the run asked for no sampling: the pair is the fault path's");
        });
    }

    #endregion

    #region Sampling Tests

    [Test]
    public void OnlyAPackageThatCameBackTwiceIsKnownToBeSampledTest()
    {
        // A rate of a quarter over four packages asks for one twice; which one is the platform's
        // choice. The one that came back twice, with one answer, is the one: its problem goes out
        // again, and the packages that came back once each, as they were asked, do not.
        var (set, options, packages) = Packages(4, X_ABOVE_ONE, rate: 0.25);
        var returned = new List<SmtResultPackageData> { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), Lost(packages[0], FAULT_CODE) };
        returned.AddRange(packages.Skip(1).Select(package => m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO))));

        var repairs = m_rig.Repairs(packages, returned, rate: 0.25);
        var repaired = repairs.SelectMany(again => Copies(again, 2, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, [], [], rate: 0.25);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1), "the problem of the package that came back twice, and none of the packages asked for once");
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(Enumerable.Range(1, 3).Select(index => Row(run, index).GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(FanOut(run, "main").GetProperty("selected").GetInt32(), Is.EqualTo(1));
            Assert.That(FanOut(run, "main").GetProperty("complete").GetInt32(), Is.Zero, "the first fan-out came back short");
            Assert.That(Sampling(run), Is.EqualTo("as_asked"), "and the repair round made up for it");
        });
    }

    [Test]
    public void ARateThatAsksForEveryPackageKnowsTheOneWhoseSecondCopyLeftNothingTest()
    {
        // The second copy left no result at all (a value the platform left out, a node gone with
        // no other machine to take its work): only one result came back under the ticket. A rate
        // that asks for every package says the package was asked for twice all the same.
        var (set, options, packages) = Packages(1, X_ABOVE_ONE, rate: 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)) };

        var repairs = m_rig.Repairs(packages, returned, rate: 1);
        var repaired = repairs.SelectMany(again => Copies(again, 2, () => SmtSettleRig.Sat(X_TWO))).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, [], [], rate: 1);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(CountOf(run, "repairs_by_reason", COPIES_SHORT), Is.EqualTo(1));
            Assert.That(Sampling(run), Is.EqualTo("as_asked"));
        });
    }

    [Test]
    public void APackageAskedForTwiceOfWhichOneResultCameBackLeavesTheSamplingShortTest()
    {
        // A rate of a quarter over four packages asks for one twice, and every package came back
        // once: the host cannot tell which one lost its second copy, sends nothing again, and says
        // that the sampling did not happen.
        var (set, options, packages) = Packages(4, X_ABOVE_ONE, rate: 0.25);
        var returned = packages.Select(package => m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO))).ToList();

        var repairs = m_rig.Repairs(packages, returned, rate: 0.25);
        var run = Settle(set, options, packages, returned, repairs, [], [], [], rate: 0.25);

        Assert.Multiple(() =>
        {
            Assert.That(repairs, Is.Empty, "nothing tells the package asked for twice from the others");
            Assert.That(Enumerable.Range(0, 4).Select(index => Row(run, index).GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(Sampling(run), Is.EqualTo("short"));
        });
    }

    #endregion

    #region Tools

    /// <summary>A set of problems of one text, read with options of a rate, each problem a package of its own.</summary>
    private (string Set, SmtOptionsData Options, List<SmtTaskPackageData> Packages) Packages(int count, string smt2, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of(Enumerable.Range(0, count).Select(index => JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = $"p{index}", ["smt2"] = smt2 })));
        var packages = SmtSettleRig.Read(set, options).Select(problem => m_rig.Publish([problem], options)).ToList();
        return (set, options, packages);
    }

    /// <summary>A copy of a package that came back without an answer to its problems, in one of the ways a copy is lost.</summary>
    private SmtResultPackageData Lost(SmtTaskPackageData package, string loss)
    {
        return loss switch
        {
            FAULT_CODE => SmtRunRig.Fault(m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), SmtNodeFaults.MEMORY_INSUFFICIENT),
            REFUSED_RESULT => m_rig.Answer(package, _ =>
            {
                var row = SmtSettleRig.Sat(X_TWO);
                row.Digest = new string('0', 64);
                return row;
            }, seal: false),
            NODE_FAULT_ROW => m_rig.Answer(package, _ => SmtSettleRig.Backstop()),
            _ => throw new ArgumentOutOfRangeException(nameof(loss), loss, "not a way a copy is lost")
        };
    }

    /// <summary>Copies of a package's answer, each a result package of its own.</summary>
    private List<SmtResultPackageData> Copies(SmtTaskPackageData package, int count, Func<SmtResultData> answer)
    {
        return Enumerable.Range(0, count).Select(copy => m_rig.Answer(package, problem => answer())).ToList();
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired,
        IReadOnlyList<SmtTaskPackageData> disputes, IReadOnlyList<SmtResultPackageData> settled, double rate)
    {
        var run = m_rig.Settle(set, options, issued, returned, repairs, repaired, disputes, settled, rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    private static JsonElement Row(SmtSettledDocuments run, int index)
    {
        return run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).Single(row => row.GetProperty("index").GetInt32() == index);
    }

    private static JsonElement Integrity(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Integrity).RootElement;
    }

    private static JsonElement Report(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Report).RootElement;
    }

    private static JsonElement FanOut(SmtSettledDocuments run, string round)
    {
        return Integrity(run).GetProperty("fan_outs").EnumerateArray().Single(fanOut => fanOut.GetProperty("round").GetString() == round);
    }

    private static string? Sampling(SmtSettledDocuments run)
    {
        var report = Report(run).GetProperty("run").GetProperty("sampling").GetString();
        Assert.That(Integrity(run).GetProperty("summary").GetProperty("sampling").GetString(), Is.EqualTo(report), "the report and the integrity document say one word");
        return report;
    }

    private static int Count(SmtSettledDocuments run, string name)
    {
        return Integrity(run).GetProperty("summary").GetProperty(name).GetInt32();
    }

    /// <summary>A count of a map of the integrity document's summary: 0 when the key is not there.</summary>
    private static int CountOf(SmtSettledDocuments run, string map, string key)
    {
        return Integrity(run).GetProperty("summary").GetProperty(map).TryGetProperty(key, out var count) ? count.GetInt32() : 0;
    }

    #endregion
}
