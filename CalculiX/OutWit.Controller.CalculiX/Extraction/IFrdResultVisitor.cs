namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Receives a ccx .frd file as the reader streams it: the nodes of the
/// geometry section when asked for, then every nodal result block row by
/// row. Nothing is held back by the reader - a visitor that keeps only its
/// running figures reads a file of any size in constant memory.
/// </summary>
public interface IFrdResultVisitor
{
    #region Functions

    /// <summary>
    /// A node of the geometry section; called only when <see cref="WantsGeometry"/> is true.
    /// </summary>
    /// <param name="id">Node id.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    void Node(int id, double x, double y, double z);

    /// <summary>
    /// A nodal result block begins; its rows follow, then <see cref="EndBlock"/>.
    /// A block without rows is never begun.
    /// </summary>
    /// <param name="header">The block's dataset, step, time and components.</param>
    void BeginBlock(FrdBlockHeader header);

    /// <summary>
    /// One row of the current block, every component a finite number.
    /// </summary>
    /// <param name="node">Node id.</param>
    /// <param name="values">The stored components, in header order. The array is the
    /// reader's and is overwritten by the next row: copy what must be kept.</param>
    void Row(int node, double[] values);

    /// <summary>
    /// One row of the current block the reader could not take: a component that is
    /// not a number (ccx writes "nan" or "-nan(ind)" where a solve blew up) or not finite.
    /// </summary>
    /// <param name="node">Node id.</param>
    /// <param name="text">The offending field, trimmed.</param>
    void BrokenRow(int node, string text);

    /// <summary>
    /// The current block ends.
    /// </summary>
    void EndBlock();

    #endregion

    #region Properties

    /// <summary>True to receive the nodes of the geometry section.</summary>
    bool WantsGeometry { get; }

    #endregion
}
