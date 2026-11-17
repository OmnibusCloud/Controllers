using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Reads a taskset once, a line at a time: every line is parsed and held to the schema, every
/// task's limits are resolved and clamped, the batches the tasks make are counted by the
/// planner's own rule, and everything Split refuses a taskset for is collected. Memory holds
/// one line and one task, whatever the taskset's size. With a spool the tasks that will be
/// planned are kept on the disk for the packages to be cut from; without one (a preflight)
/// nothing is kept at all.
/// </summary>
public static class VerifyTasksetScanner
{
    #region Functions

    /// <summary>Scans a taskset.</summary>
    /// <param name="taskset">The taskset, at its start.</param>
    /// <param name="options">The run options, or null for the defaults.</param>
    /// <param name="spool">Where the tasks to plan are kept, or null when only the account is wanted.</param>
    /// <returns>What the taskset holds and what is wrong with it.</returns>
    public static VerifyTasksetScan Scan(Stream taskset, VerifyOptionsData? options, VerifyTaskSpool? spool)
    {
        var scan = new VerifyTasksetScan();
        var lines = new VerifyTasksetLines(taskset);
        var indices = new VerifyTaskIndices();
        var meters = new Dictionary<string, VerifyBatchMeter>(StringComparer.Ordinal);
        var notes = new List<string>();

        var batchSize = VerifyTasksetPlanner.BatchSize(options, out var sizeNote);
        if (sizeNote != null)
            scan.Notes.Add(sizeNote);

        while (lines.MoveNext())
        {
            VerifyTaskData task;
            try
            {
                task = VerifyTasksetParser.ParseLine(lines, indices);
            }
            catch (FormatException exception)
            {
                scan.Errors.Add($"line {lines.Number}: {exception.Message}");
                continue;
            }

            scan.Tasks++;
            foreach (var source in task.Sources)
                scan.InputBytes += source.Content.Length;

            if (task.Limits is { WallTimeMs: > 0 })
                scan.SetsWallTime = true;

            // A runtime this controller does not know has no node that could run it: its tasks
            // are counted, and never kept (their ids are the lab's to invent).
            var known = VerifyRuntimeCatalog.Find(task.RuntimeId) != null;
            var group = scan.Name(task.RuntimeId, known);

            notes.Clear();
            var error = VerifyTasksetPlanner.Admit(task, options?.DefaultLimits, notes, out var bytes, out var output);
            scan.Notes.AddRange(notes);
            if (error != null)
            {
                scan.PlanErrors.Add(error);
                continue;
            }

            if (group == null)
                continue;

            var meter = meters.TryGetValue(group, out var open) ? open : new VerifyBatchMeter(batchSize);
            if (!meter.Fits(bytes, output))
                meter.Reset();

            if (meter.Tasks == 0)
                scan.Batches++;

            meter.Add(bytes, output);
            meters[group] = meter;

            if (!known)
                continue;

            scan.Planned++;

            // A taskset that is refused already is not planned: nothing more of it is kept.
            if (spool != null && scan.Errors.Count == 0 && scan.UnknownTasks == 0 && scan.PlanErrors.Count == 0)
                spool.Add(task);
        }

        if (VerifyTasksetChecks.SetsWallTime(options))
            scan.SetsWallTime = true;

        return scan;
    }

    #endregion
}
