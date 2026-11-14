using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.Grid.Model;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Diagnostics;

/// <summary>
/// The busiest node's share as the preflight states it: the plan follows the Grid controller's
/// allocator step for step (held together here against <see cref="WitGridTaskAllocator"/> itself on
/// pools and runs of many shapes), and the answer is a share and counts, never a node.
/// </summary>
[TestFixture]
public sealed class CorpusSharePlannerTests
{
    #region Constants

    private const long MIB = 1024L * 1024;

    #endregion

    #region Tests

    [Test]
    public void EqualNodesShareEqualShardsEquallyTest()
    {
        var plan = CorpusSharePlanner.Plan([Node(10), Node(10), Node(10), Node(10)], Sizes(8, 64 * MIB), 0);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PoolNodes, Is.EqualTo(4));
            Assert.That(plan.PlannedNodes, Is.EqualTo(4));
            Assert.That(plan.BusiestShare, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(plan.UnmeasuredNodes, Is.Zero);
            Assert.That(plan.Describe(), Is.EqualTo("Work split as the platform would plan it now: 4 of 4 node(s) take shards; the busiest node's share of the work is 25.0 %."));
        });
    }

    [Test]
    public void AFastNodeTakesTheShareItsSpeedEarnsTest()
    {
        // One node three times as fast as each of the two others: it takes three fifths of the work.
        var plan = CorpusSharePlanner.Plan([Node(30), Node(10), Node(10)], Sizes(20, 64 * MIB), 0);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PlannedNodes, Is.EqualTo(3));
            Assert.That(plan.BusiestShare, Is.EqualTo(0.6).Within(1e-12));
        });
    }

    [Test]
    public void ASlowNodeIsLeftOutWhenItWouldNotHelpTest()
    {
        // Two shards, a fast node and a very slow one: the slow one would finish its shard last.
        var plan = CorpusSharePlanner.Plan([Node(100), Node(1)], Sizes(2, 64 * MIB), 0);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PoolNodes, Is.EqualTo(2));
            Assert.That(plan.PlannedNodes, Is.EqualTo(1));
            Assert.That(plan.BusiestShare, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void ASampledShardsCopyGoesToAnotherNodeTest()
    {
        // One shard and its copy: two executions, never on one node, half the work each.
        var plan = CorpusSharePlanner.Plan([Node(100), Node(1)], Sizes(1, 64 * MIB), 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PlannedNodes, Is.EqualTo(2));
            Assert.That(plan.BusiestShare, Is.EqualTo(0.5));
        });
    }

    [Test]
    public void NoPoolOrNoWorkIsSaidPlainlyTest()
    {
        var none = CorpusSharePlanner.Plan([], Sizes(3, MIB), 0);
        var empty = CorpusSharePlanner.Plan([Node(5)], [], 0);
        var unmeasured = CorpusSharePlanner.Plan([new CorpusShareNode(1, false), new CorpusShareNode(1, false)], Sizes(2, 0), 0);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.EqualTo(CorpusSharePlan.NONE));
            Assert.That(none.Describe(), Does.StartWith("Work split: no node can take Corpus shards"));
            Assert.That(empty.PoolNodes, Is.EqualTo(1));
            Assert.That(empty.PlannedNodes, Is.Zero);
            Assert.That(unmeasured.PlannedNodes, Is.EqualTo(2), "two unmeasured nodes at the same default rate, two shards of a typical size");
            Assert.That(unmeasured.UnmeasuredNodes, Is.EqualTo(2));
            Assert.That(unmeasured.Describe(), Does.Contain("2 of them have not measured themselves"));
            Assert.That(CorpusSharePlanner.Work(0), Is.EqualTo(CorpusSharePlanner.DEFAULT_SHARD_MEGABYTES));
            Assert.That(CorpusSharePlanner.Work(10), Is.EqualTo(1), "at least one unit, as the shard task estimates it");
        });
    }

    /// <summary>The plan against the Grid controller's allocator on random pools and runs: the same node count and the same busiest share.</summary>
    [Test]
    public void ThePlanIsTheAllocatorsTest()
    {
        var random = new Random(20261114);
        for (var round = 0; round < 300; round++)
        {
            var nodes = Enumerable.Range(0, random.Next(1, 9))
                .Select(_ => new CorpusShareNode(Math.Round(1 + random.NextDouble() * 40, 2), random.Next(5) > 0))
                .ToList();
            var sizes = Enumerable.Range(0, random.Next(1, 40))
                .Select(_ => random.Next(6) == 0 ? 0 : (long)(random.Next(1, 1024) * MIB))
                .ToList();
            var sampled = nodes.Count > 1 ? random.Next(0, Math.Min(sizes.Count, 4) + 1) : 0;
            var rate = sampled == 0 ? 0 : (double)sampled / sizes.Count;

            var plan = CorpusSharePlanner.Plan(nodes, sizes, rate);
            var (planned, busiest) = Allocator(nodes, sizes, sampled);

            Assert.Multiple(() =>
            {
                Assert.That(plan.PlannedNodes, Is.EqualTo(planned), $"round {round}: {nodes.Count} nodes, {sizes.Count} shards, {sampled} sampled");
                Assert.That(plan.BusiestShare, Is.EqualTo(busiest).Within(1e-9), $"round {round}");
            });
        }
    }

    #endregion

    #region Tools

    /// <summary>The Grid controller's own plan of the same run, with the largest shards sampled as the share plan assumes.</summary>
    private static (int Planned, double Busiest) Allocator(IReadOnlyList<CorpusShareNode> nodes, IReadOnlyList<long> sizes, int sampled)
    {
        var activityNodes = nodes.Select(node => (IWitEngineActivityNode)new ShareTestNode(node)).ToList();
        var copied = Enumerable.Range(0, sizes.Count).OrderByDescending(index => CorpusSharePlanner.Work(sizes[index])).Take(sampled).ToHashSet();

        var tasks = new List<WitGridTask>();
        for (var index = 0; index < sizes.Count; index++)
            tasks.Add(new WitGridTask { Work = CorpusSharePlanner.Work(sizes[index]), ReplicaOf = copied.Contains(index) ? index : null });
        foreach (var index in copied.Order())
            tasks.Add(new WitGridTask { Work = CorpusSharePlanner.Work(sizes[index]), ReplicaOf = index });

        var groups = WitGridTaskAllocator.Allocate(activityNodes, tasks.OrderByDescending(task => task.Work).ToList());
        var total = groups.Sum(group => group.TotalWork);
        return (groups.Count, groups.Max(group => group.TotalWork) / total);
    }

    private static CorpusShareNode Node(double rate)
    {
        return new CorpusShareNode(rate, true);
    }

    private static List<long> Sizes(int count, long bytes)
    {
        return Enumerable.Repeat(bytes, count).ToList();
    }

    #endregion

    #region Types

    private sealed class ShareTestNode(CorpusShareNode node) : IWitEngineActivityNode
    {
        public Guid NodeId { get; } = Guid.NewGuid();

        public IWitBenchmarkResult BenchmarkResult { get; } = new WitBenchmarkResult { Rate = node.Rate, Iterations = node.Measured ? 3 : 0 };
    }

    #endregion
}
