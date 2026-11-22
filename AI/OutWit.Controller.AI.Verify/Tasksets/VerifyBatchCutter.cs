using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Cuts the tasks of one runtime, given one at a time, into batches by the rule of
/// <see cref="VerifyBatchMeter"/>: one batch is held, never the taskset.
/// </summary>
public sealed class VerifyBatchCutter
{
    #region Fields

    private readonly string m_runtimeId;
    private readonly string m_runtimeSha256;
    private readonly VerifyLimitsData? m_defaultLimits;
    private readonly int m_maxDegree;
    private VerifyBatchMeter m_meter;
    private VerifyTaskBatchData? m_current;

    #endregion

    #region Constructors

    /// <summary>A cutter for the tasks of a runtime.</summary>
    /// <param name="runtimeId">The runtime the tasks name.</param>
    /// <param name="options">The run options, or null for the defaults.</param>
    /// <param name="batchSize">The most tasks a batch holds (<see cref="VerifyTasksetPlanner.BatchSize"/>).</param>
    public VerifyBatchCutter(string runtimeId, VerifyOptionsData? options, int batchSize)
    {
        m_runtimeId = runtimeId;
        m_runtimeSha256 = VerifyRuntimeCatalog.Find(runtimeId)?.Sha256 ?? "";
        m_defaultLimits = options?.DefaultLimits;
        m_maxDegree = Math.Max(0, options?.MaxDegree ?? 0);
        m_meter = new VerifyBatchMeter(batchSize);
    }

    #endregion

    #region Functions

    /// <summary>Adds a task.</summary>
    /// <param name="task">The task, its limits resolved and clamped.</param>
    /// <param name="bytes">The task's text, in characters.</param>
    /// <param name="output">The output the task may keep, by its caps.</param>
    /// <returns>The batch the task closed, or null while the open one takes it.</returns>
    public VerifyTaskBatchData? Add(VerifyTaskData task, long bytes, long output)
    {
        VerifyTaskBatchData? closed = null;
        if (m_current == null || !m_meter.Fits(bytes, output))
        {
            closed = m_current;
            m_current = new VerifyTaskBatchData
            {
                RuntimeId = m_runtimeId,
                DefaultLimits = m_defaultLimits,
                MaxDegree = m_maxDegree,
                RuntimeSha256 = m_runtimeSha256
            };
            m_meter.Reset();
        }

        m_current.Tasks.Add(task);
        m_meter.Add(bytes, output);
        return closed;
    }

    /// <summary>Ends the runtime's tasks.</summary>
    /// <returns>The last batch, or null when no task was added.</returns>
    public VerifyTaskBatchData? Finish()
    {
        var last = m_current;
        m_current = null;
        m_meter.Reset();
        return last;
    }

    #endregion
}
