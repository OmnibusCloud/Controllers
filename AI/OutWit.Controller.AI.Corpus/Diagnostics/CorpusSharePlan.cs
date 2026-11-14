using System.Globalization;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// How a run's work would be split over the pool now, as counts and a share: never a node.
/// </summary>
/// <param name="PoolNodes">Nodes that can take Corpus shards for the submitter now.</param>
/// <param name="PlannedNodes">Of them, the nodes the plan gives shards to.</param>
/// <param name="UnmeasuredNodes">Planned nodes that have not measured themselves (planned at the slowest measured rate).</param>
/// <param name="BusiestShare">The busiest node's share of the work, 0 to 1 (0 when nothing is planned).</param>
public sealed record CorpusSharePlan(int PoolNodes, int PlannedNodes, int UnmeasuredNodes, double BusiestShare)
{
    #region Constants

    /// <summary>A plan of an empty pool or an empty run.</summary>
    public static readonly CorpusSharePlan NONE = new(0, 0, 0, 0);

    #endregion

    #region Functions

    /// <summary>The preflight's line.</summary>
    /// <returns>For example "Work split as the platform would plan it now: 5 of 6 nodes take shards; the busiest node's share of the work is 23.4 %."</returns>
    public string Describe()
    {
        if (PoolNodes == 0)
            return "Work split: no node can take Corpus shards for this submitter now, so the busiest node's share is not known.";

        if (PlannedNodes == 0)
            return $"Work split: {PoolNodes} node(s) in the pool, nothing to plan.";

        var line = string.Create(CultureInfo.InvariantCulture,
            $"Work split as the platform would plan it now: {PlannedNodes} of {PoolNodes} node(s) take shards; the busiest node's share of the work is {BusiestShare * 100:0.0} %.");
        if (UnmeasuredNodes > 0)
            line += $" {UnmeasuredNodes} of them have not measured themselves (such a node is planned at the slowest measured rate, or at the engine's default when no node has measured itself).";

        return line;
    }

    #endregion
}
