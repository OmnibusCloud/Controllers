using System.Globalization;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Diagnostics;

/// <summary>
/// Asks the host which nodes would take Smt work and adds them up. The host answers for the
/// submitter's own scope and for the activity that solves a package, with the requirements it
/// declares applied (a machine below <see cref="SmtSandboxDegree.MIN_RAM_MB"/> is not counted; a
/// node that has reported no capabilities is, as the planner admits it), so the totals are those
/// of the pool a run submitted now would be planned on.
/// </summary>
internal static class SmtPoolProbe
{
    #region Functions

    /// <summary>Adds up the nodes that can take Smt work now.</summary>
    /// <param name="nodes">The host's nodes manager, or null when the process has none (a node, a test host).</param>
    /// <returns>The totals, or null when the host cannot say.</returns>
    public static async Task<SmtPoolSummary?> SummarizeAsync(IWitNodesManager? nodes)
    {
        if (nodes == null)
            return null;

        var compatible = await nodes.GetCompatibleNodes<WitActivitySmtSolvePackage>(WitProcessingOptions.Default);

        return Summarize(compatible.Select(node => node.BenchmarkResult));
    }

    /// <summary>Adds up the benchmark results of the pool's nodes.</summary>
    /// <param name="benchmarks">One result per node; a node that has not measured itself carries another unit or none.</param>
    /// <returns>The totals.</returns>
    public static SmtPoolSummary Summarize(IEnumerable<IWitBenchmarkResult> benchmarks)
    {
        var nodes = 0;
        var measured = 0;
        var withDegree = 0;
        var workers = 0;
        var rate = 0.0;

        foreach (var benchmark in benchmarks)
        {
            nodes++;

            if (!string.Equals(benchmark.Unit, SmtBenchmark.UNIT, StringComparison.Ordinal) || benchmark.Rate <= 0)
                continue;

            measured++;
            rate += benchmark.Rate;

            if (benchmark.Custom != null
                && benchmark.Custom.TryGetValue(SmtBenchmark.DEGREE_KEY, out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var degree)
                && degree > 0)
            {
                withDegree++;
                workers += degree;
            }
        }

        // The degree travels in the benchmark's custom values, which a host may not keep.
        return new SmtPoolSummary(nodes, measured, measured > 0 && withDegree == measured ? workers : null, rate);
    }

    #endregion
}
