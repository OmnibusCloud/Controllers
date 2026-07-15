using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Turns a flat task list into the chunks Grid.ForEach fans out: tasks are grouped by
/// runtime affinity (a batch shares one runtime module, compiled once per chunk) and each
/// group is split into batches of the configured size. Per-task limits are resolved
/// against the options default and clamped to the host ceilings here, so nodes receive
/// ready-to-run batches.
/// </summary>
public static class VerifyTasksetPlanner
{
    #region Constants

    public const int DEFAULT_BATCH_SIZE = 32;

    #endregion

    #region Functions

    public static VerifyTasksetPlan Plan(IReadOnlyList<VerifyTaskData> tasks, VerifyOptionsData? options)
    {
        var batchSize = options?.BatchSize is > 0 ? options.BatchSize : DEFAULT_BATCH_SIZE;
        var defaultLimits = options?.DefaultLimits;
        var notes = new List<string>();

        var batches = new List<VerifyTaskBatchData>();

        // Group by runtime, preserving first-seen order for reproducible chunking.
        foreach (var group in tasks.GroupBy(task => task.RuntimeId, StringComparer.Ordinal))
        {
            var runtimeTasks = group.ToList();
            for (var offset = 0; offset < runtimeTasks.Count; offset += batchSize)
            {
                var chunk = runtimeTasks.Skip(offset).Take(batchSize).ToList();
                foreach (var task in chunk)
                {
                    var resolved = VerifySandboxDefaults.Resolve(task.Limits, defaultLimits);
                    var (clamped, clampNotes) = VerifyLimitCeilings.Clamp(resolved, task.TaskIndex);
                    task.Limits = clamped;
                    notes.AddRange(clampNotes);
                }

                batches.Add(new VerifyTaskBatchData
                {
                    RuntimeId = group.Key,
                    DefaultLimits = defaultLimits,
                    Tasks = chunk
                });
            }
        }

        return new VerifyTasksetPlan(batches, notes);
    }

    #endregion
}

public sealed record VerifyTasksetPlan(List<VerifyTaskBatchData> Batches, List<string> Notes);
