using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Where a batch ends: at the configured size, and earlier when its tasks' text reaches what a
/// package may hold or their output caps together reach what a node holds for one batch. The
/// one statement of the rule: the planner cuts batches by it, and the preflight counts the
/// batches a taskset makes by it without holding a task.
/// </summary>
public struct VerifyBatchMeter
{
    #region Fields

    private readonly int m_batchSize;
    private long m_bytes;
    private long m_output;

    #endregion

    #region Constructors

    /// <summary>A meter for batches of a size.</summary>
    /// <param name="batchSize">The most tasks a batch holds.</param>
    public VerifyBatchMeter(int batchSize)
    {
        m_batchSize = batchSize;
    }

    #endregion

    #region Functions

    /// <summary>Whether a task still fits the batch the meter stands for. An empty batch takes any task.</summary>
    /// <param name="bytes">The task's text, in characters.</param>
    /// <param name="output">The output the task may keep, by its caps.</param>
    /// <returns>False when the task starts a new batch.</returns>
    public readonly bool Fits(long bytes, long output)
    {
        return Tasks == 0
               || (Tasks < m_batchSize
                   && m_bytes + bytes <= VerifyPackagePlanner.MAX_PACKAGE_BYTES
                   && m_output + output <= VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES);
    }

    /// <summary>Counts a task into the batch.</summary>
    /// <param name="bytes">The task's text, in characters.</param>
    /// <param name="output">The output the task may keep, by its caps.</param>
    public void Add(long bytes, long output)
    {
        Tasks++;
        m_bytes += bytes;
        m_output += output;
    }

    /// <summary>Starts a new batch.</summary>
    public void Reset()
    {
        Tasks = 0;
        m_bytes = 0;
        m_output = 0;
    }

    #endregion

    #region Properties

    /// <summary>The tasks in the batch so far.</summary>
    public int Tasks { get; private set; }

    #endregion
}
