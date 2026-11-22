namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Where a package ends: at the number of tasks a run of its size aims for, and earlier when
/// the text of its batches reaches what a package may hold or their output caps together reach
/// what a result package may grow to. The one statement of the rule, for the plan of batches in
/// memory (<see cref="VerifyPackagePlanner.Plan"/>) and for the publisher that is given one
/// batch at a time (<see cref="VerifyPackagePublisher"/>).
/// </summary>
public struct VerifyPackageMeter
{
    #region Fields

    private readonly int m_tasksPerPackage;
    private int m_tasks;
    private long m_bytes;
    private long m_output;

    #endregion

    #region Constructors

    /// <summary>A meter for the packages of a run.</summary>
    /// <param name="totalTasks">The tasks of the whole run.</param>
    public VerifyPackageMeter(long totalTasks)
    {
        m_tasksPerPackage = VerifyPackagePlanner.TasksPerPackage(totalTasks);
    }

    #endregion

    #region Functions

    /// <summary>Whether a batch still fits the package the meter stands for. An empty package takes any batch.</summary>
    /// <param name="tasks">The batch's tasks.</param>
    /// <param name="bytes">The batch's text, in characters.</param>
    /// <param name="output">The output the batch's tasks may keep, by their caps.</param>
    /// <returns>False when the batch starts a new package.</returns>
    public readonly bool Fits(int tasks, long bytes, long output)
    {
        return Batches == 0
               || (m_tasks + tasks <= m_tasksPerPackage
                   && m_bytes + bytes <= VerifyPackagePlanner.MAX_PACKAGE_BYTES
                   && m_output + output <= VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES);
    }

    /// <summary>Counts a batch into the package.</summary>
    /// <param name="tasks">The batch's tasks.</param>
    /// <param name="bytes">The batch's text, in characters.</param>
    /// <param name="output">The output the batch's tasks may keep, by their caps.</param>
    public void Add(int tasks, long bytes, long output)
    {
        Batches++;
        m_tasks += tasks;
        m_bytes += bytes;
        m_output += output;
    }

    /// <summary>Starts a new package.</summary>
    public void Reset()
    {
        Batches = 0;
        m_tasks = 0;
        m_bytes = 0;
        m_output = 0;
    }

    #endregion

    #region Properties

    /// <summary>The batches in the package so far.</summary>
    public int Batches { get; private set; }

    #endregion
}
