using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Groups the task batches of a run into packages, the items Grid.ForEach places on nodes. A
/// package travels as one blob, so it should not be tiny (a transfer per handful of tasks) and
/// it must not be coarse (the split between nodes is only as fine as the items). The rule:
/// one batch per package while the run is small, and about <see cref="TARGET_PACKAGES"/>
/// packages once it is large, within the caps on a package's tasks, on the text of its tasks
/// and on the output its tasks may keep. The caps hold for every package: a batch is itself cut
/// within them where the taskset is planned (<c>VerifyTasksetPlanner</c>), so none arrives here
/// that is larger than a package may be.
/// </summary>
public static class VerifyPackagePlanner
{
    #region Constants

    /// <summary>The number of packages a large run is cut into: fine enough for a split by rate over hundreds of nodes.</summary>
    public const int TARGET_PACKAGES = 1024;

    /// <summary>The most tasks one package holds: what a node loses and redoes with one package stays seconds of work.</summary>
    public const int MAX_PACKAGE_TASKS = 4096;

    /// <summary>The most task text one package holds, in characters, before compression; also the most one task may have.</summary>
    public const long MAX_PACKAGE_BYTES = 8L * 1024 * 1024;

    /// <summary>
    /// The most output the tasks of one package may keep together, by their caps
    /// (<see cref="OutputBytes"/>): what a result package can grow to on a node's disk and what
    /// the server reads back from it.
    /// </summary>
    public const long MAX_PACKAGE_OUTPUT_BYTES = 256L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>Groups consecutive batches into packages.</summary>
    /// <param name="batches">The batches, in plan order, each within a package's caps.</param>
    /// <returns>The packages, each a run of consecutive batches; a batch is never split.</returns>
    public static List<List<VerifyTaskBatchData>> Plan(IReadOnlyList<VerifyTaskBatchData> batches)
    {
        var meter = new VerifyPackageMeter(batches.Sum(batch => (long)batch.Tasks.Count));

        var packages = new List<List<VerifyTaskBatchData>>();
        var current = new List<VerifyTaskBatchData>();

        foreach (var batch in batches)
        {
            var batchBytes = EstimateBytes(batch);
            var batchOutput = OutputBytes(batch);

            if (!meter.Fits(batch.Tasks.Count, batchBytes, batchOutput))
            {
                packages.Add(current);
                current = [];
                meter.Reset();
            }

            current.Add(batch);
            meter.Add(batch.Tasks.Count, batchBytes, batchOutput);
        }

        if (current.Count > 0)
            packages.Add(current);

        return packages;
    }

    /// <summary>
    /// The tasks a package of a run aims for: one batch a package while the run is small, about
    /// <see cref="TARGET_PACKAGES"/> packages once it is large, and never more than
    /// <see cref="MAX_PACKAGE_TASKS"/> a package.
    /// </summary>
    /// <param name="totalTasks">The tasks of the whole run.</param>
    /// <returns>The most tasks a package holds before the next batch starts another.</returns>
    public static int TasksPerPackage(long totalTasks)
    {
        return (int)Math.Clamp((totalTasks + TARGET_PACKAGES - 1) / TARGET_PACKAGES, 1, MAX_PACKAGE_TASKS);
    }

    /// <summary>The text a batch carries, in characters: sources, arguments, inputs and expected outputs.</summary>
    /// <param name="batch">The batch.</param>
    /// <returns>The estimate; exact sizes are known only after serialization.</returns>
    public static long EstimateBytes(VerifyTaskBatchData batch)
    {
        return batch.Tasks.Sum(EstimateBytes);
    }

    /// <summary>The text a task carries, in characters: sources, arguments, inputs and expected outputs.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The estimate.</returns>
    public static long EstimateBytes(VerifyTaskData task)
    {
        long bytes = task.EntryPoint.Length + task.RuntimeId.Length + (task.Stdin?.Length ?? 0);

        foreach (var source in task.Sources)
            bytes += source.Name.Length + source.Content.Length;

        foreach (var argument in task.Args)
            bytes += argument.Length;

        if (task.Suite == null)
            return bytes;

        foreach (var suiteCase in task.Suite.Cases)
        {
            bytes += (suiteCase.Stdin?.Length ?? 0) + (suiteCase.ExpectedStdout?.Length ?? 0);

            foreach (var argument in suiteCase.Args)
                bytes += argument.Length;
        }

        return bytes;
    }

    /// <summary>The output the tasks of a batch may keep together, by their caps.</summary>
    /// <param name="batch">The batch.</param>
    /// <returns>Bytes.</returns>
    public static long OutputBytes(VerifyTaskBatchData batch)
    {
        return batch.Tasks.Sum(task => OutputBytes(VerifyLimitCeilings.Effective(task.Limits, batch.DefaultLimits)));
    }

    /// <summary>
    /// The output one task may keep, by its caps: its stdout cap twice (the cases of a suite
    /// share one cap, and the last case's stdout is also the task's) and its stderr cap.
    /// </summary>
    /// <param name="limits">The task's effective limits.</param>
    /// <returns>Bytes.</returns>
    public static long OutputBytes(VerifyLimitsData limits)
    {
        return 2L * limits.StdoutLimitBytes + limits.StderrLimitBytes;
    }

    #endregion
}
