namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One shell or beam element of a deck: its id, its normalized type and its
/// nodes in the order of the <c>*ELEMENT</c> card (corner nodes first, then
/// the mid-side nodes of a quadratic element, as ccx numbers them).
/// </summary>
public sealed class DeckElement
{
    #region Constructors

    /// <summary>
    /// Creates the element.
    /// </summary>
    /// <param name="id">Element id.</param>
    /// <param name="type">Normalized element type, e.g. "S8R", "B31".</param>
    /// <param name="nodes">Node ids in card order.</param>
    public DeckElement(int id, string type, int[] nodes)
    {
        Id = id;
        Type = type;
        Nodes = nodes;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The number of nodes an element type carries, for the types whose
    /// section stress the controller recovers or must refuse.
    /// </summary>
    /// <param name="type">Normalized element type.</param>
    /// <returns>The node count; 0 for a type that is not a shell or beam.</returns>
    public static int NodeCountOf(string type)
    {
        return type switch
        {
            "S3" => 3,
            "S4" or "S4R" => 4,
            "S6" => 6,
            "S8" or "S8R" => 8,
            "B31" or "B31R" => 2,
            "B32" or "B32R" => 3,
            _ => 0
        };
    }

    /// <summary>
    /// The number of corner nodes of a shell type: 3 for a triangle, 4 for a quadrilateral.
    /// </summary>
    /// <param name="type">Normalized shell type.</param>
    /// <returns>3 or 4; 0 for a type that is not a shell.</returns>
    public static int CornersOf(string type)
    {
        return type switch
        {
            "S3" or "S6" => 3,
            "S4" or "S4R" or "S8" or "S8R" => 4,
            _ => 0
        };
    }

    /// <summary>
    /// The corner nodes of an edge of a shell: edge k runs from corner k to
    /// corner k + 1; it is face k + 3 of the element in ccx's numbering
    /// (S3 for the first edge; S1 and S2 are the bottom and top faces).
    /// </summary>
    /// <param name="edge">0-based edge index.</param>
    /// <returns>The edge's two corner node ids, in element order.</returns>
    public (int A, int B) EdgeCorners(int edge)
    {
        var corners = CornersOf(Type);
        return (Nodes[edge], Nodes[(edge + 1) % corners]);
    }

    public override string ToString()
    {
        return $"{Type} {Id}: {string.Join(", ", Nodes)}";
    }

    #endregion

    #region Properties

    /// <summary>Element id.</summary>
    public int Id { get; }

    /// <summary>Normalized element type, e.g. "S8R", "B31".</summary>
    public string Type { get; }

    /// <summary>Node ids in card order.</summary>
    public int[] Nodes { get; }

    /// <summary>True for a shell element (S*).</summary>
    public bool IsShell => Type.StartsWith('S');

    #endregion
}
