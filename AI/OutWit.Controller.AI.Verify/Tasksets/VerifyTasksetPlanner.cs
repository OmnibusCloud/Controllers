using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Turns a flat task list into the chunks Grid.ForEach fans out: tasks are grouped by
/// runtime affinity (a batch shares one runtime module, compiled once per chunk) and each
/// group is cut into batches. Per-task limits are resolved against the options default and
/// clamped to the ceilings here, so nodes receive ready-to-run batches (a node clamps again:
/// it holds itself to the ceilings whoever made the batch).
/// <para>
/// A batch ends at the configured size, and earlier when its tasks' text reaches what a package
/// may hold or their output caps together reach <see cref="MAX_BATCH_OUTPUT_BYTES"/>: a batch is
/// never larger than a package may be, and a node never holds more results in memory than that.
/// A single task whose text is larger than a package is refused by its index.
/// </para>
/// <para>
/// <see cref="Plan"/> plans tasks that are in memory. A taskset blob is planned without being
/// held: the scan (<see cref="VerifyTasksetScanner"/>) admits its tasks one at a time and the
/// packages are cut from a spool (<see cref="VerifyTaskSpool"/>), by the same rule
/// (<see cref="VerifyBatchMeter"/>, <see cref="VerifyBatchCutter"/>).
/// </para>
/// </summary>
public static class VerifyTasksetPlanner
{
    #region Constants

    /// <summary>Tasks per batch when the options set none.</summary>
    public const int DEFAULT_BATCH_SIZE = 32;

    /// <summary>
    /// The most output the tasks of one batch may keep together, by their caps: a node holds
    /// the results of one batch in memory before it writes them out.
    /// </summary>
    public const long MAX_BATCH_OUTPUT_BYTES = 64L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>Groups the tasks by runtime and cuts each group into batches, resolving and clamping every task's limits.</summary>
    /// <param name="tasks">The tasks, in taskset order.</param>
    /// <param name="options">The run options, or null for the defaults.</param>
    /// <returns>The batches, the clamping notes and the tasks that cannot be planned.</returns>
    public static VerifyTasksetPlan Plan(IReadOnlyList<VerifyTaskData> tasks, VerifyOptionsData? options)
    {
        var notes = new List<string>();
        var errors = new List<string>();
        var batches = new List<VerifyTaskBatchData>();

        var batchSize = BatchSize(options, out var sizeNote);
        if (sizeNote != null)
            notes.Add(sizeNote);

        // Group by runtime, preserving first-seen order for reproducible chunking.
        foreach (var group in tasks.GroupBy(task => task.RuntimeId, StringComparer.Ordinal))
        {
            var cutter = new VerifyBatchCutter(group.Key, options, batchSize);

            foreach (var task in group)
            {
                var error = Admit(task, options?.DefaultLimits, notes, out var bytes, out var output);
                if (error != null)
                {
                    errors.Add(error);
                    continue;
                }

                if (cutter.Add(task, bytes, output) is { } closed)
                    batches.Add(closed);
            }

            if (cutter.Finish() is { } last)
                batches.Add(last);
        }

        return new VerifyTasksetPlan(batches, notes, errors);
    }

    /// <summary>
    /// The most tasks a batch of a run holds: what the options ask for, the default when they
    /// ask for none, and never more than a node takes.
    /// </summary>
    /// <param name="options">The run options, or null for the defaults.</param>
    /// <param name="note">What to tell the lab when the size it asked for was clamped; else null.</param>
    /// <returns>The batch size.</returns>
    public static int BatchSize(VerifyOptionsData? options, out string? note)
    {
        note = null;

        var batchSize = options?.BatchSize is > 0 ? options.BatchSize : DEFAULT_BATCH_SIZE;
        if (batchSize > VerifyBatchExecutor.MAX_BATCH_TASKS)
        {
            note = $"batch size {batchSize} clamped to {VerifyBatchExecutor.MAX_BATCH_TASKS}";
            batchSize = VerifyBatchExecutor.MAX_BATCH_TASKS;
        }

        return batchSize;
    }

    /// <summary>
    /// Makes a task ready for a batch: its limits are resolved against the defaults and clamped
    /// to the ceilings (the task is changed), and its text and its output caps are measured.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="defaultLimits">The run's default limits, or null.</param>
    /// <param name="notes">Receives one note per limit that was clamped.</param>
    /// <param name="bytes">The task's text, in characters.</param>
    /// <param name="output">The output the task may keep, by its caps.</param>
    /// <returns>Why the task cannot be planned (its text is larger than a package), or null.</returns>
    public static string? Admit(VerifyTaskData task, VerifyLimitsData? defaultLimits, ICollection<string> notes, out long bytes, out long output)
    {
        var resolved = VerifySandboxDefaults.Resolve(task.Limits, defaultLimits);
        var (clamped, clampNotes) = VerifyLimitCeilings.Clamp(resolved, task.TaskIndex);
        task.Limits = clamped;
        foreach (var note in clampNotes)
            notes.Add(note);

        output = VerifyPackagePlanner.OutputBytes(clamped);
        bytes = VerifyPackagePlanner.EstimateBytes(task);

        return bytes > VerifyPackagePlanner.MAX_PACKAGE_BYTES
            ? $"task {task.TaskIndex}: its text is {bytes} characters; a task may have {VerifyPackagePlanner.MAX_PACKAGE_BYTES}"
            : null;
    }

    #endregion
}
