namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The visitor behind <see cref="FrdResultReader.Read(string)"/>: holds every
/// block of the file in memory as <see cref="FrdResultBlock"/>s - for small
/// files and tests, never for the extractor. Rows the reader could not take
/// are left out of their block.
/// </summary>
public sealed class FrdBlockCollector : IFrdResultVisitor
{
    #region Fields

    private FrdResultBlock? m_current;

    #endregion

    #region IFrdResultVisitor

    public void Node(int id, double x, double y, double z)
    {
    }

    public void BeginBlock(FrdBlockHeader header)
    {
        m_current = new FrdResultBlock(header);
        Blocks.Add(m_current);
    }

    public void Row(int node, double[] values)
    {
        m_current!.Values[node] = (double[])values.Clone();
    }

    public void BrokenRow(int node, string text)
    {
    }

    public void EndBlock()
    {
        // A block whose every row was broken carries nothing - dropped, as a
        // block without rows is never begun.
        if (m_current != null && m_current.Values.Count == 0)
            Blocks.Remove(m_current);

        m_current = null;
    }

    public bool WantsGeometry => false;

    #endregion

    #region Properties

    /// <summary>The blocks read so far, in file order.</summary>
    public List<FrdResultBlock> Blocks { get; } = [];

    #endregion
}
