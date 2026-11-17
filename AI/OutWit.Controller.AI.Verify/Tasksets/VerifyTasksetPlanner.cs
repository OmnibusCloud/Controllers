using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
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

        var batchSize = options?.BatchSize is > 0 ? options.BatchSize : DEFAULT_BATCH_SIZE;
        if (batchSize > VerifyBatchExecutor.MAX_BATCH_TASKS)
        {
            notes.Add($"batch size {batchSize} clamped to {VerifyBatchExecutor.MAX_BATCH_TASKS}");
            batchSize = VerifyBatchExecutor.MAX_BATCH_TASKS;
        }

        var defaultLimits = options?.DefaultLimits;
        var maxDegree = Math.Max(0, options?.MaxDegree ?? 0);
        var batches = new List<VerifyTaskBatchData>();

        // Group by runtime, preserving first-seen order for reproducible chunking.
        foreach (var group in tasks.GroupBy(task => task.RuntimeId, StringComparer.Ordinal))
        {
            var runtimeSha256 = VerifyRuntimeCatalog.Find(group.Key)?.Sha256 ?? "";
            VerifyTaskBatchData? current = null;
            long currentBytes = 0;
            long currentOutput = 0;

            foreach (var task in group)
            {
                var resolved = VerifySandboxDefaults.Resolve(task.Limits, defaultLimits);
                var (clamped, clampNotes) = VerifyLimitCeilings.Clamp(resolved, task.TaskIndex);
                task.Limits = clamped;
                notes.AddRange(clampNotes);

                var bytes = VerifyPackagePlanner.EstimateBytes(task);
                if (bytes > VerifyPackagePlanner.MAX_PACKAGE_BYTES)
                {
                    errors.Add($"task {task.TaskIndex}: its text is {bytes} characters; a task may have {VerifyPackagePlanner.MAX_PACKAGE_BYTES}");
                    continue;
                }

                var output = VerifyPackagePlanner.OutputBytes(clamped);
                if (current != null
                    && (current.Tasks.Count >= batchSize
                        || currentBytes + bytes > VerifyPackagePlanner.MAX_PACKAGE_BYTES
                        || currentOutput + output > MAX_BATCH_OUTPUT_BYTES))
                {
                    current = null;
                }

                if (current == null)
                {
                    current = new VerifyTaskBatchData
                    {
                        RuntimeId = group.Key,
                        DefaultLimits = defaultLimits,
                        MaxDegree = maxDegree,
                        RuntimeSha256 = runtimeSha256
                    };
                    batches.Add(current);
                    currentBytes = 0;
                    currentOutput = 0;
                }

                current.Tasks.Add(task);
                currentBytes += bytes;
                currentOutput += output;
            }
        }

        return new VerifyTasksetPlan(batches, notes, errors);
    }

    #endregion
}
