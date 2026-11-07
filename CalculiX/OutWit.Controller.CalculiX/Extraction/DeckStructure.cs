namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What the section-stress recovery needs to know about a deck's shells and
/// beams: the elements with their nodes, the thickness of every shell and
/// the cross section of every beam, the cards that rule a recovery out (a
/// material that is not linear elastic, a composite or nodal-thickness shell
/// section, section prints of the deck's own) and the output the controller
/// added before the solve (<c>*EL FILE, SECTION FORCES</c>, one surface per
/// shell edge). Read only for a deck that writes <c>OUTPUT=2D</c> and has
/// shells or beams.
/// </summary>
public sealed class DeckStructure
{
    #region Functions

    /// <summary>
    /// The shell elements of the deck by their edges: the unordered pair of
    /// an edge's corner nodes → the elements on that edge and the edge's
    /// index in each.
    /// </summary>
    /// <returns>Edge → (element, 0-based edge index) for every shell element of a type with known corners.</returns>
    public Dictionary<(int, int), List<(DeckElement Element, int Edge)>> ShellEdges()
    {
        var edges = new Dictionary<(int, int), List<(DeckElement, int)>>();
        foreach (var element in Shells.Values.OrderBy(element => element.Id))
        {
            var corners = DeckElement.CornersOf(element.Type);
            for (var edge = 0; edge < corners; edge++)
            {
                var (a, b) = element.EdgeCorners(edge);
                var key = (System.Math.Min(a, b), System.Math.Max(a, b));
                if (!edges.TryGetValue(key, out var owners))
                    edges[key] = owners = [];

                owners.Add((element, edge));
            }
        }

        return edges;
    }

    #endregion

    #region Properties

    /// <summary>Shell elements (S*) by id.</summary>
    public Dictionary<int, DeckElement> Shells { get; } = [];

    /// <summary>Beam elements (B*) by id.</summary>
    public Dictionary<int, DeckElement> Beams { get; } = [];

    /// <summary>
    /// The nodes of the other elements ccx maps onto the 1D/2D mesh under
    /// OUTPUT=2D - membranes, plane and axisymmetric elements, trusses - where
    /// their stress is averaged with that of any beam on the same node.
    /// </summary>
    public HashSet<int> OtherMappedNodes { get; } = [];

    /// <summary>True when the deck has truss elements (T3D*), which ccx expands as beams.</summary>
    public bool HasTrusses { get; set; }

    /// <summary>Shell or beam types the recovery does not know (its element tables leave them out).</summary>
    public SortedSet<string> UnknownTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>The thickness of every shell element a plain <c>*SHELL SECTION</c> covers.</summary>
    public Dictionary<int, double> ShellThickness { get; } = [];

    /// <summary>The positions of the beam elements' nodes.</summary>
    public Dictionary<int, Vector3D> BeamNodeCoordinates { get; } = [];

    /// <summary>The cross section of every beam element a <c>*BEAM SECTION</c> or <c>*BEAM GENERAL SECTION</c> covers.</summary>
    public Dictionary<int, DeckBeamSection> BeamSections { get; } = [];

    /// <summary>
    /// The first shell section the recovery cannot take (a <c>COMPOSITE</c>
    /// or a <c>NODAL THICKNESS</c> section), as words for a note; null when none.
    /// </summary>
    public string? ShellSectionProblem { get; set; }

    /// <summary>
    /// The first beam section card the recovery cannot take (<c>NODAL
    /// THICKNESS</c>), as words for a note; null when none.
    /// </summary>
    public string? BeamSectionProblem { get; set; }

    /// <summary>The first material card that is not linear elastic (e.g. "*PLASTIC"); null when none.</summary>
    public string? NonlinearMaterial { get; set; }

    /// <summary>True when the deck has <c>*SECTION PRINT</c> cards of its own.</summary>
    public bool OwnSectionPrints { get; set; }

    /// <summary>True when a file card asks for the stress (<c>S</c>).</summary>
    public bool StressRequested { get; set; }

    /// <summary>True when an element file card writes in local frames (<c>GLOBAL=NO</c>).</summary>
    public bool LocalElementOutput { get; set; }

    /// <summary>True when an <c>*EL FILE</c> card of the first step carries <c>SECTION FORCES</c>.</summary>
    public bool SectionForces { get; set; }

    /// <summary>
    /// The controller's edge surfaces (<see cref="CcxSectionStressPlan.EDGE_SURFACE_PREFIX"/>):
    /// surface name → the element and its ccx face number (3 for the first edge).
    /// </summary>
    public Dictionary<string, (int Element, int Face)> EdgeSurfaces { get; } = new(StringComparer.OrdinalIgnoreCase);

    #endregion
}
