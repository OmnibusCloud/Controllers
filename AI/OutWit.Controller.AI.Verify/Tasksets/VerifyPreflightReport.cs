using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// What a preflight answers, made from a scan of the taskset: the counts, the runtimes, and
/// messages for everything Split would refuse the taskset for and everything it would clamp.
/// The answer travels in a job variable, so it is bounded whatever the taskset: every kind of
/// message is cut to <see cref="VerifyTasksetLimits.MAX_MESSAGES"/> lines of at most
/// <see cref="VerifyTasksetLimits.MAX_MESSAGE_LENGTH"/> characters with one line for the rest,
/// and the runtime ids listed are at most <see cref="VerifyTasksetLimits.MAX_LISTED_RUNTIMES"/>
/// unknown ones beside the controller's own.
/// </summary>
public static class VerifyPreflightReport
{
    #region Constants

    /// <summary>What a preflight says of a taskset without a task.</summary>
    public const string EMPTY = "Taskset is empty.";

    #endregion

    #region Functions

    /// <summary>Makes the preflight's answer.</summary>
    /// <param name="scan">What a pass over the taskset found.</param>
    /// <returns>The answer.</returns>
    public static VerifyPreflightData Of(VerifyTasksetScan scan)
    {
        var report = new VerifyPreflightData
        {
            // What Split would refuse is not well formed.
            WellFormed = scan.Accepted,
            TaskCount = scan.Tasks,
            BatchCount = scan.Batches,
            EstimatedInputBytes = scan.InputBytes,
            RuntimeIds = [.. scan.RuntimeIds],
            UnknownRuntimeIds = [.. scan.UnknownRuntimeIds],
            Messages = scan.Errors.ToList(VerifyTasksetParser.MALFORMED_LINES)
        };

        if (scan.Tasks == 0 && scan.Errors.Count == 0)
            report.Messages.Add(EMPTY);

        foreach (var unknown in scan.UnknownRuntimeIds)
            report.Messages.Add($"runtime '{unknown}' is not known to this build: the taskset would be refused.");

        if (scan.MoreUnknownRuntimes)
            report.Messages.Add(VerifyTasksetScan.MORE_UNKNOWN_RUNTIMES);

        if (scan.SetsWallTime)
            report.Messages.Add(VerifyTasksetChecks.WALL_TIME_NOTE);

        report.Messages.AddRange(scan.Notes.ToList(VerifyTasksetScan.NOTES));
        report.Messages.AddRange(scan.PlanErrors.ToList(VerifyTasksetScan.UNPLANNED));

        return report;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The most messages a preflight's answer can have, whatever the taskset: three kinds of
    /// bounded lists with a line for the rest of each, the listed unknown runtimes with theirs,
    /// the wall-time note and the note of an empty taskset.
    /// </summary>
    public static int MaxMessages => 3 * (VerifyTasksetLimits.MAX_MESSAGES + 1) + VerifyTasksetLimits.MAX_LISTED_RUNTIMES + 3;

    #endregion
}
