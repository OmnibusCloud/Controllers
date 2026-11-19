using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>The counts of a settled run, as the report's summary carries them.</summary>
public sealed class VerifySettlementSummary
{
    #region Constants

    /// <summary>The word for a run whose script did not state the sampling it asked for.</summary>
    public const string SAMPLING_NOT_STATED = "not_stated";

    #endregion

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

    /// <summary>The share of the packages the run asked to execute twice, 0 to 1; null when the script did not state it.</summary>
    public double? SamplingRate { get; set; }

    /// <summary>
    /// Whether the sampling that was asked for happened: <c>not_stated</c>, <c>none</c>,
    /// <c>as_asked</c>, or <c>short</c> when fewer packages came back with two accepted answers
    /// than the rate asks for.
    /// </summary>
    public string Sampling { get; set; } = SAMPLING_NOT_STATED;

    /// <summary>The task packages no accepted answer came back for, every fan-out together.</summary>
    public int UnansweredPackages { get; set; }

    #endregion
}
