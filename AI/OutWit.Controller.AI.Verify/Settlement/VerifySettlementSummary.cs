using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>The counts of a settled run, as the report's summary carries them.</summary>
public sealed class VerifySettlementSummary
{
    #region Functions

    /// <summary>The summary of tasks that were all settled by one result each, with nothing refused and nothing disputed.</summary>
    /// <param name="tasks">The tasks.</param>
    /// <returns>The summary.</returns>
    public static VerifySettlementSummary Of(IEnumerable<VerifySettledTask> tasks)
    {
        var summary = new VerifySettlementSummary();
        foreach (var task in tasks)
            summary.Count(task.Verdict);

        return summary;
    }

    /// <summary>Counts one task under its verdict.</summary>
    /// <param name="verdict">The verdict.</param>
    public void Count(VerifyVerdict verdict)
    {
        Total++;
        Verdicts[verdict] = Verdicts.GetValueOrDefault(verdict) + 1;
    }

    #endregion

    #region Properties

    /// <summary>The tasks of the run.</summary>
    public int Total { get; private set; }

    /// <summary>The tasks under each verdict.</summary>
    public Dictionary<VerifyVerdict, int> Verdicts { get; } = new();

    /// <summary>The tasks with verdict Pass.</summary>
    public int Pass => Verdicts.GetValueOrDefault(VerifyVerdict.Pass);

    /// <summary>The result packages the server refused, every round and reason together.</summary>
    public int RejectedResults { get; set; }

    /// <summary>The tasks whose executions disagreed.</summary>
    public int Disputes { get; set; }

    /// <summary>The disputes three more executions settled.</summary>
    public int DisputesResolved { get; set; }

    #endregion
}
