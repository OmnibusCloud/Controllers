namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// A place in a later round, read beside the first: the round holds shards of the same job in
/// the same ascending order, so the tasks of a shard are either the next ones or not there.
/// A task for a shard the job does not have is nobody's: its values are dropped as under a
/// ticket nobody gave out.
/// </summary>
internal sealed class CorpusLedgerCursor : IDisposable
{
    #region Fields

    private readonly IEnumerator<CorpusRoundPackage>? m_packages;
    private readonly CorpusLedgerTally m_tally;
    private CorpusRoundPackage? m_package;
    private int m_at;

    #endregion

    #region Constructors

    /// <summary>Opens the round at its start.</summary>
    /// <param name="source">The round, or null when it has not run: the cursor then has nothing.</param>
    /// <param name="tally">Receives what the round dropped.</param>
    public CorpusLedgerCursor(CorpusRoundSource? source, CorpusLedgerTally tally)
    {
        m_packages = source?.Read().GetEnumerator();
        m_tally = tally;
    }

    #endregion

    #region Functions

    /// <summary>The round's tasks for a shard, when it has any. Tasks for shards before it that the job does not have are passed over and their values dropped.</summary>
    /// <param name="index">The shard's index; asked for in ascending order.</param>
    /// <param name="slots">The tasks of the round for the shard, with what came back under each.</param>
    /// <param name="package">The package the shard's tasks are in, or null.</param>
    /// <returns>True when the round holds the shard.</returns>
    public bool Take(int index, out List<CorpusRoundSlot> slots, out CorpusRoundPackage? package)
    {
        slots = [];
        package = null;

        while (Current() is { } slot && slot.Task.TaskIndex <= index)
        {
            if (slot.Task.TaskIndex == index)
            {
                slots.Add(slot);
                package = m_package;
            }
            else
                Orphan(slot);

            m_at++;
        }

        return slots.Count > 0;
    }

    /// <summary>Ends the reading: what is left of the round belongs to no shard of the job.</summary>
    public void Finish()
    {
        while (Current() is { } slot)
        {
            Orphan(slot);
            m_at++;
        }
    }

    #endregion

    #region Tools

    private CorpusRoundSlot? Current()
    {
        while (m_packages != null && (m_package == null || m_at >= m_package.Slots.Count))
        {
            if (!m_packages.MoveNext())
            {
                m_package = null;
                return null;
            }

            m_package = m_packages.Current;
            m_at = 0;
            m_tally.Add(m_package.Dropped);
        }

        return m_package?.Slots[m_at];
    }

    private void Orphan(CorpusRoundSlot slot)
    {
        if (slot.Values.Count > 0)
            m_tally.Reject(CorpusLedger.REJECTED_UNKNOWN_TICKET, slot.Values.Count);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        m_packages?.Dispose();
    }

    #endregion
}
