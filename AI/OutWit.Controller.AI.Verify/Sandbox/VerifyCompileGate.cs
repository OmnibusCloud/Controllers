namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The memory the engine may take, in one process, for the compiles of tasks' modules that take
/// much of it. What a compile takes is in no task's memory cap, so the workers of a batch must
/// not all be in a large one at once: a compile enters with the memory it may take
/// (<see cref="VerifyModuleShapeReport.CompileMemoryBytes"/>) and waits while the compiles
/// already inside leave no room for it. Several compiles of ordinary programs run side by side;
/// one at the bounds of a task's module runs alone. The gate's size is what a node sets aside
/// for it when it decides how many tasks run at once (<see cref="VerifyBatchExecutor.MemoryAside"/>).
/// </summary>
internal sealed class VerifyCompileGate
{
    #region Fields

    private readonly object m_lock = new();
    private readonly long m_capacity;
    private long m_taken;

    #endregion

    #region Constructors

    /// <summary>
    /// A gate of a size.
    /// </summary>
    /// <param name="capacityBytes">The memory the compiles inside may take together, bytes.</param>
    public VerifyCompileGate(long capacityBytes)
    {
        m_capacity = Math.Max(1, capacityBytes);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Enters with the memory a compile may take, waiting while there is no room for it. A
    /// compile that may take more than the gate holds takes all of it, and so runs alone.
    /// </summary>
    /// <param name="bytes">The memory the compile may take, bytes.</param>
    /// <returns>The compile's place in the gate; dispose it when the compile is over.</returns>
    public IDisposable Enter(long bytes)
    {
        var take = Math.Clamp(bytes, 1, m_capacity);

        lock (m_lock)
        {
            while (m_taken + take > m_capacity)
                Monitor.Wait(m_lock);

            m_taken += take;
        }

        return new Place(this, take);
    }

    private void Leave(long taken)
    {
        lock (m_lock)
        {
            m_taken -= taken;
            Monitor.PulseAll(m_lock);
        }
    }

    #endregion

    #region Properties

    /// <summary>The memory the compiles inside may take together, bytes.</summary>
    public long Capacity => m_capacity;

    /// <summary>The memory the compiles inside right now were let in with, bytes.</summary>
    public long Taken
    {
        get
        {
            lock (m_lock)
            {
                return m_taken;
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed class Place(VerifyCompileGate gate, long taken) : IDisposable
    {
        private bool m_left;

        public void Dispose()
        {
            if (m_left)
                return;

            m_left = true;
            gate.Leave(taken);
        }
    }

    #endregion
}
