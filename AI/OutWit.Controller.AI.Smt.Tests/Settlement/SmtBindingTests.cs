using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using static OutWit.Controller.AI.Smt.Tests.Mock.SmtSettledReader;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// A result counts as a machine only when the platform told the host which package it was
/// returned for. The rig tells the host as Grid 1.4.0 tells the return collection of a fan-out
/// (through the collection's own method), or tells it nothing, as an older Grid. A result returned
/// for another package than the one it names is refused and its package goes out again; bound
/// answers of one fan-out are two machines; unbound ones are one machine's word however many they
/// are, and are still compared by digest; a dispute whose answers are bound is settled on a pool
/// of two machines; the collection's method takes anything without throwing, from several threads
/// at once. Every document is held to its schema.
/// </summary>
[TestFixture]
public sealed class SmtBindingTests
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

    #region Twice Tests

    [Test]
    public void AResultReturnedForAnotherPackageIsRefusedAndThatPackageGoesOutAgainTest()
    {
        // One node, rate 0, packages A and B: it answers A twice, the second time for B's task,
        // and B not at all.
        var (set, options, packages) = Packages(2, 0);
        var (a, b) = (packages[0], packages[1]);
        var returned = new[] { m_rig.Answer(a, _ => SmtSettleRig.Unsat()), m_rig.Answer(a, _ => SmtSettleRig.Unsat(), returnedFor: b) };

        var repairs = m_rig.Repairs(packages, returned, 0);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 0);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1), "B's problem goes out again, and A's does not");
            Assert.That(CountOf(run, "rejected", "other_package"), Is.EqualTo(1), "the second answer of A is no answer");
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY), "A rests on one machine");
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(1));
            Assert.That(Indices(run, "unsat_one_copy"), Is.EqualTo(new[] { 0 }), "and its unsat is listed as one machine's");
            Assert.That(Count(run, "unsat_one_copy"), Is.EqualTo(1));
            Assert.That(Row(run, 1).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), "B is answered by the repair pair");
            Assert.That(Count(run, "repairs"), Is.EqualTo(1));
            Assert.That(CountOf(run, "repairs_by_reason", "other_package"), Is.EqualTo(1), "what became of B's task");
            Assert.That(Word(run, "binding"), Is.EqualTo(SmtRunWords.BOUND));
            Assert.That(Count(run, "results_bound"), Is.EqualTo(4));
            Assert.That(Count(run, "results_unbound"), Is.Zero);
            Assert.That(Report(run).GetProperty("run").GetProperty("binding").GetString(), Is.EqualTo(SmtRunWords.BOUND), "the report says the same word");
        });
    }

    [Test]
    public void TheSameResultsUnboundAreNeverCopiesTest()
    {
        // The rounds filled as an older Grid fills them: nobody tells the host which package's
        // task a result came back for.
        m_rig.Binds = false;
        var (set, options, packages) = Packages(2, 0);
        var (a, b) = (packages[0], packages[1]);
        var returned = new[] { m_rig.Answer(a, _ => SmtSettleRig.Unsat()), m_rig.Answer(a, _ => SmtSettleRig.Unsat(), returnedFor: b) };

        var repairs = m_rig.Repairs(packages, returned, 0);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 0);
        var settledBy = Rows(run).Select(row => row.GetProperty("settled_by").GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(settledBy, Has.None.EqualTo(SmtRunWords.COPIES), "no answer is a second machine");
            Assert.That(settledBy, Has.All.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(2), "both results of A are taken and compared");
            Assert.That(Row(run, 0).GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(Indices(run, "unsat_one_copy"), Is.EqualTo(new[] { 0 }));
            Assert.That(Report(run).GetProperty("copies").GetProperty("two_or_more").GetInt32(), Is.Zero);
            Assert.That(Count(run, "rejected_results"), Is.Zero);
            Assert.That(Word(run, "binding"), Is.EqualTo(SmtRunWords.UNBOUND));
            Assert.That(Count(run, "results_unbound"), Is.EqualTo(4));
            Assert.That(Report(run).GetProperty("run").GetProperty("binding").GetString(), Is.EqualTo(SmtRunWords.UNBOUND));
        });
    }

    [Test]
    public void AResultToldForTwoPackagesIsBoundToNeitherTest()
    {
        var (set, options, packages) = Packages(2, 1);
        var told = m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO));
        SmtRunRig.Bind(packages[1], told);

        var returned = new[] { told, m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[1], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[1], _ => SmtSettleRig.Sat(X_TWO)) };
        var repairs = m_rig.Repairs(packages, returned, 1);
        var run = Settle(set, options, packages, returned, repairs, [], 1);

        Assert.Multiple(() =>
        {
            Assert.That(SmtRunRig.BoundTicket(told), Is.Empty, "no package's ticket");
            Assert.That(CountOf(run, "rejected", "other_package"), Is.EqualTo(1));
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1), "the package it named lost a copy and goes out again");
            Assert.That(Row(run, 1).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), "the other package keeps its two machines");
        });
    }

    #endregion

    #region Machines Tests

    [TestCase(1, 1.0)]
    [TestCase(4, 0.5)]
    [TestCase(6, 0.25)]
    [TestCase(10, 0.05)]
    [TestCase(20, 0.3)]
    public void AnHonestSampledPackageBoundIsSettledByCopiesTest(int count, double rate)
    {
        var (set, options, packages) = Packages(count, rate);
        var selected = SmtRunRig.Selected(count, rate);
        var returned = packages.SelectMany((package, at) => Enumerable.Range(0, at < selected ? 2 : 1).Select(_ => m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)))).ToList();

        var repairs = m_rig.Repairs(packages, returned, rate);
        var run = Settle(set, options, packages, returned, repairs, [], rate);

        Assert.Multiple(() =>
        {
            Assert.That(repairs, Is.Empty, "every problem has the answers it was asked for");
            for (var index = 0; index < count; index++)
                Assert.That(Row(run, index).GetProperty("settled_by").GetString(), Is.EqualTo(index < selected ? SmtRunWords.COPIES : SmtRunWords.ONE_COPY), $"problem {index}");

            Assert.That(Report(run).GetProperty("copies").GetProperty("two_or_more").GetInt32(), Is.EqualTo(selected));
            Assert.That(Count(run, "one_copy"), Is.EqualTo(count - selected));
            Assert.That(Word(run, "sampling"), Is.EqualTo("as_asked"));
            Assert.That(Word(run, "binding"), Is.EqualTo(SmtRunWords.BOUND));
            Assert.That(Count(run, "results_bound"), Is.EqualTo(count + selected));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheRepairPairIsTwoMachinesOnlyWhenBoundTest(bool bound)
    {
        // A package asked for twice lost a copy (its node's fault): its problem goes out again,
        // and the repair pair agrees.
        m_rig.Binds = bound;
        var (set, options, packages) = Packages(1, 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), SmtRunRig.Fault(m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), SmtNodeFaults.MEMORY_INSUFFICIENT) };

        var repairs = m_rig.Repairs(packages, returned, 1);
        var repaired = repairs.SelectMany(again => new[] { m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(again, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();
        var run = Settle(set, options, packages, returned, repairs, repaired, 1);

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Sum(again => again.ProblemCount), Is.EqualTo(1));
            Assert.That(CountOf(run, "repairs_by_reason", SmtRunRig.CopiesShort), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(3));
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(bound ? SmtRunWords.COPIES : SmtRunWords.ONE_COPY));
            Assert.That(Word(run, "binding"), Is.EqualTo(bound ? SmtRunWords.BOUND : SmtRunWords.UNBOUND));
        });
    }

    [Test]
    public void OneBoundAnswerAndOneUnboundAreOneMachinesWordTest()
    {
        var (set, options, packages) = Packages(1, 1);
        var bound = m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO));
        m_rig.Binds = false;
        var unbound = m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO));

        var run = Settle(set, options, packages, [bound, unbound], [], [], 1);

        Assert.Multiple(() =>
        {
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(Row(run, 0).GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(Word(run, "binding"), Is.EqualTo(SmtRunWords.MIXED));
            Assert.That(Count(run, "results_bound"), Is.EqualTo(1));
            Assert.That(Count(run, "results_unbound"), Is.EqualTo(1));
        });
    }

    [Test]
    public void OnAnOlderGridCopiesAreStillComparedByDigestTest()
    {
        // Unbound copies that differ are a dispute, as they always were; the dispute's majority
        // settles it; and nothing is settled by copies.
        m_rig.Binds = false;
        var (set, options, packages) = Packages(1, 1);
        var returned = new[] { m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(packages[0], _ => SmtSettleRig.Sat(X_THREE)) };

        var disputes = m_rig.Disputes(packages, returned, [], []);
        var settled = new[] { m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(X_THREE)) };
        var run = Settle(set, options, packages, returned, [], [], 1, disputes, settled);
        var dispute = Integrity(run).GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(disputes.Sum(package => package.ProblemCount), Is.EqualTo(1), "the copies' digests differ");
            Assert.That(dispute.GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.DIGESTS_DIFFER));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(SmtRunWords.RESOLVED), "three answers of one package: none can be a second answer of a node that robbed another");
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(Rows(run).Select(row => row.GetProperty("settled_by").GetString()), Has.None.EqualTo(SmtRunWords.COPIES));
            Assert.That(Word(run, "binding"), Is.EqualTo(SmtRunWords.UNBOUND));
        });
    }

    #endregion

    #region Dispute Tests

    [TestCase(true, SmtRunWords.RESOLVED)]
    [TestCase(false, SmtRunWords.SURPLUS_COPIES)]
    public void ADisputeOnAPoolOfTwoMachinesIsSettledWhenItsAnswersAreBoundTest(bool bound, string outcome)
    {
        // Two one-problem packages whose copies disagreed go to three more machines; the pool has
        // two, so Grid asks each package of them twice, and both answer each package with one
        // digest. Bound, no answer can be a second one: the two machines settle the dispute.
        // Unbound, each package is one answer short, so one answer of each other package may be a
        // node's second.
        m_rig.Binds = bound;
        var (set, options, packages) = Packages(2, 1);
        var returned = packages.SelectMany(package => new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)) }).ToList();
        var disputes = SmtSettleRig.Read(set, options).Select(problem => m_rig.Publish([problem], options)).ToList();
        var settled = disputes.SelectMany(package => new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)), m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO)) }).ToList();

        var run = Settle(set, options, packages, returned, [], [], 1, disputes, settled);
        var records = Integrity(run).GetProperty("disputes").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records.Select(record => record.GetProperty("cause").GetString()), Has.All.EqualTo(SmtRunWords.DIGESTS_DIFFER));
            Assert.That(records.Select(record => record.GetProperty("outcome").GetString()), Is.EqualTo(new[] { outcome, outcome }));
            Assert.That(CountOf(run, "dispute_outcomes", outcome), Is.EqualTo(2));
            foreach (var index in new[] { 0, 1 })
            {
                var row = Row(run, index);
                Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(bound ? SmtResultWords.OK : SmtRunWords.DISPUTED), $"problem {index}");
                Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(bound ? SmtRunWords.DISPUTE : ""), $"problem {index}");
                Assert.That(row.GetProperty("code").GetString(), Is.EqualTo(bound ? "" : SmtRunWords.SURPLUS_COPIES), $"problem {index}");
            }

            Assert.That(Count(run, "lies"), Is.EqualTo(bound ? 2 : 0), "settled, the answer of the first round that differs is a lie");
        });
    }

    #endregion

    #region Telling Tests

    [Test]
    public void TheCollectionsMethodTakesWhatGridNeverGivesWithoutThrowingTest()
    {
        var (_, _, packages) = Packages(1, 0);
        var result = m_rig.Answer(packages[0], _ => SmtSettleRig.Unsat(), returnedFor: null);
        var untold = UnboundAnswer(packages[0]);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => SmtRunRig.Tell(null, null));
            Assert.DoesNotThrow(() => SmtRunRig.Tell(null, untold), "no item");
            Assert.DoesNotThrow(() => SmtRunRig.Tell(packages[0], null), "no value");
            Assert.DoesNotThrow(() => SmtRunRig.Tell("a text", untold), "an item of another type");
            Assert.DoesNotThrow(() => SmtRunRig.Tell(42, untold), "a boxed value");
            Assert.DoesNotThrow(() => SmtRunRig.Tell(packages[0], "a text"), "a value of another type");
            Assert.DoesNotThrow(() => SmtRunRig.Tell(packages[0], new SmtProblemData()), "a value of a model type that is no result");
            Assert.DoesNotThrow(() => SmtRunRig.Tell(new SmtTaskPackageData { Ticket = null! }, untold), "a package without a ticket");
            Assert.That(SmtRunRig.BoundTicket(untold), Is.Empty, "a package without a ticket binds the result to none");
            Assert.That(SmtRunRig.BoundTicket(result), Is.EqualTo(packages[0].Ticket), "the rig's own telling");
        });
    }

    [Test]
    public void TheCollectionsMethodMayBeCalledFromSeveralThreadsAtOnceTest()
    {
        var (_, _, packages) = Packages(2, 0);
        var results = Enumerable.Range(0, 2_000).Select(_ => new SmtResultPackageData { Ticket = packages[0].Ticket }).ToList();

        // Every result told for its package by several threads; every tenth told for the other one as well.
        Parallel.For(0, results.Count * 3, new ParallelOptions { MaxDegreeOfParallelism = 8 }, at =>
        {
            var result = results[at % results.Count];
            var other = at / results.Count == 2 && at % 10 == 0;
            SmtRunRig.Tell(other ? packages[1] : packages[0], result);
        });

        Assert.Multiple(() =>
        {
            for (var at = 0; at < results.Count; at++)
                Assert.That(SmtRunRig.BoundTicket(results[at]), Is.EqualTo(at % 10 == 0 ? "" : packages[0].Ticket), $"result {at}");
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

    /// <summary>An answer the host was told nothing of.</summary>
    private SmtResultPackageData UnboundAnswer(SmtTaskPackageData package)
    {
        var binds = m_rig.Binds;
        m_rig.Binds = false;
        try
        {
            return m_rig.Answer(package, _ => SmtSettleRig.Unsat());
        }
        finally
        {
            m_rig.Binds = binds;
        }
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired, double rate,
        IReadOnlyList<SmtTaskPackageData>? disputes = null, IReadOnlyList<SmtResultPackageData>? settled = null)
    {
        var run = m_rig.Settle(set, options, issued, returned, repairs, repaired, disputes ?? [], settled ?? [], rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    #endregion
}
