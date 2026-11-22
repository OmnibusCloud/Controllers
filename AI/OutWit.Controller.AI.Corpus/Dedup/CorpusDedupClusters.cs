namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// The clusters of a merge while it runs: every document alone at first, two clusters made one
/// when a document of each is similar enough to the other. A cluster is known by its lowest
/// document, whatever the order its pairs were found in, so what the merge keeps is a function
/// of the pairs and not of the order they came in. One integer a document.
/// </summary>
internal sealed class CorpusDedupClusters
{
    #region Fields

    private readonly int[] m_parent;

    #endregion

    #region Constructors

    /// <summary>Starts with every document a cluster of its own.</summary>
    /// <param name="documents">The documents of the merge.</param>
    public CorpusDedupClusters(int documents)
    {
        m_parent = new int[documents];
        for (var document = 0; document < m_parent.Length; document++)
            m_parent[document] = document;
    }

    #endregion

    #region Functions

    /// <summary>The lowest document of a document's cluster: the one the merge keeps.</summary>
    /// <param name="document">The document's number.</param>
    /// <returns>The number of the cluster's lowest document.</returns>
    public int Lowest(int document)
    {
        while (m_parent[document] != document)
        {
            m_parent[document] = m_parent[m_parent[document]];
            document = m_parent[document];
        }

        return document;
    }

    /// <summary>Makes the clusters of two documents one.</summary>
    /// <param name="first">A document.</param>
    /// <param name="second">Another.</param>
    public void Join(int first, int second)
    {
        var lowestOfFirst = Lowest(first);
        var lowestOfSecond = Lowest(second);
        if (lowestOfFirst == lowestOfSecond)
            return;

        // The lower of the two stands for both, so a cluster is always known by its lowest document.
        if (lowestOfFirst < lowestOfSecond)
            m_parent[lowestOfSecond] = lowestOfFirst;
        else
            m_parent[lowestOfFirst] = lowestOfSecond;
    }

    #endregion
}
