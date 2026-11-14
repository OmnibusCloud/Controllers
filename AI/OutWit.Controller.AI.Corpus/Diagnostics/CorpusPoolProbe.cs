using System.Globalization;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// Asks the host which nodes would take Corpus shards and adds them up. The host answers for
/// the submitter's own scope (its group, the controller fence, the node schedules), so the
/// totals are those of the pool a run submitted now would be planned on.
/// </summary>
public static class CorpusPoolProbe
{
    #region Constants

    private const string DEGREE_KEY = "degree";

    #endregion

    #region Functions

    /// <summary>Adds up the nodes that can take Corpus shards now.</summary>
    /// <param name="nodes">The host's nodes manager, or null when the process has none (a node, a test host).</param>
    /// <returns>The totals, or null when the host cannot say.</returns>
    public static async Task<CorpusPoolSummary?> SummarizeAsync(IWitNodesManager? nodes)
    {
        if (nodes == null)
            return null;

        var compatible = await nodes.GetCompatibleNodes<WitActivityCorpusProcessShard>(WitProcessingOptions.Default);

        return Summarize(compatible.Select(node => node.BenchmarkResult));
    }

    /// <summary>Adds up the benchmark results of the pool's nodes.</summary>
    /// <param name="benchmarks">One result per node; a node that has not measured itself carries another unit or none.</param>
    /// <returns>The totals.</returns>
    public static CorpusPoolSummary Summarize(IEnumerable<IWitBenchmarkResult> benchmarks)
    {
        var nodes = 0;
        var measured = 0;
        var withDegree = 0;
        var workers = 0;
        var rate = 0.0;

        foreach (var benchmark in benchmarks)
        {
            nodes++;

            if (!string.Equals(benchmark.Unit, CorpusBenchmark.UNIT, StringComparison.Ordinal) || benchmark.Rate <= 0)
                continue;

            measured++;
            rate += benchmark.Rate;

            if (benchmark.Custom != null
                && benchmark.Custom.TryGetValue(DEGREE_KEY, out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var degree)
                && degree > 0)
            {
                withDegree++;
                workers += degree;
            }
        }

        // The degree travels in the benchmark's custom values, which a host may not keep (the
        // cloud server stores a benchmark's rate and unit).
        return new CorpusPoolSummary
        {
            Nodes = nodes,
            Measured = measured,
            Workers = measured > 0 && withDegree == measured ? workers : null,
            Rate = rate
        };
    }

    #endregion
}
