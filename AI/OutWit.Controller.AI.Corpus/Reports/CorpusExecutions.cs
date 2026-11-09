using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// One Grid.ForEach call returns one result per execution, in no particular order, and a sampled
/// shard has two. This picks the execution that stands for a shard, by rule rather than by
/// arrival, so the same executions always give the same report.
/// </summary>
public static class CorpusExecutions
{
    #region Functions

    /// <summary>The execution standing for each shard, in task order.</summary>
    /// <param name="executions">All executions, any order.</param>
    /// <returns>One result per task index.</returns>
    public static List<CorpusResultData> Canonical(IEnumerable<CorpusResultData> executions)
    {
        return executions
            .GroupBy(execution => execution.TaskIndex)
            .OrderBy(group => group.Key)
            .Select(group => Pick(group))
            .ToList();
    }

    /// <summary>The execution standing for one shard: a completed one if any, then the lowest content hash.</summary>
    /// <param name="executions">The executions of one shard; at least one.</param>
    /// <returns>The chosen execution.</returns>
    public static CorpusResultData Pick(IEnumerable<CorpusResultData> executions)
    {
        return executions
            .OrderBy(execution => execution.Verdict == CorpusShardVerdict.Completed ? 0 : 1)
            .ThenBy(execution => execution.ContentSha256, StringComparer.Ordinal)
            .ThenBy(execution => execution.TextBlobId)
            .First();
    }

    #endregion
}
