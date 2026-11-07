using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What the extractor needs to know about the solved deck: its node sets,
/// its <c>*TRANSFORM</c> frames, its steps and their procedures, whether its
/// result files are written on the original 1D/2D mesh (<c>OUTPUT=2D</c>) or
/// on the expanded one, which element families it carries and how its nodes
/// are numbered. Lexical facts only - no physics, no validation.
/// </summary>
public sealed partial class DeckFacts
{
    #region Constants

    // ccx expands these onto a 3D mesh: shells, beams, trusses, membranes
    // and the plane-stress/strain/axisymmetric families. C3D* is not here.
    [GeneratedRegex(@"^(S[0-9]|B[0-9]|T[0-9]D|M3D|CPS|CPE|CAX)")]
    private static partial Regex ExpandedTypeRegex();

    [GeneratedRegex(@"^(S[0-9]|B[0-9])")]
    private static partial Regex ShellOrBeamTypeRegex();

    #endregion

    #region Functions

    /// <summary>
    /// The transform a node prints in: the last <c>*TRANSFORM</c> whose set
    /// holds the node, as ccx assigns them (a later card overrides an earlier).
    /// </summary>
    /// <param name="node">Node id.</param>
    /// <returns>The transform, or null when the node prints in the global frame.</returns>
    public DeckTransform? TransformOf(int node)
    {
        DeckTransform? found = null;
        foreach (var transform in Transforms)
        {
            if (NodeSets.TryGetValue(transform.SetName, out var nodes) && nodes.Contains(node))
                found = transform;
        }

        return found;
    }

    /// <summary>
    /// Whether an element type is one ccx expands onto a 3D mesh, so that its
    /// nodes do not appear in a 3D-output .frd under their own numbers.
    /// </summary>
    /// <param name="elementType">Normalized element type, e.g. "S8R".</param>
    /// <returns>True for shells, beams, trusses, membranes and plane/axisymmetric elements.</returns>
    public static bool IsExpandedType(string elementType)
    {
        return ExpandedTypeRegex().IsMatch(elementType);
    }

    /// <summary>
    /// Whether an element type is a shell or a beam - the families whose
    /// bending stress lives away from the mid-surface or the axis.
    /// </summary>
    /// <param name="elementType">Normalized element type, e.g. "B32".</param>
    /// <returns>True for S* and B* elements.</returns>
    public static bool IsShellOrBeamType(string elementType)
    {
        return ShellOrBeamTypeRegex().IsMatch(elementType);
    }

    #endregion

    #region Properties

    /// <summary>Node sets by name (case-insensitive): <c>*NSET</c> cards and <c>*NODE, NSET=</c> sets.</summary>
    public Dictionary<string, HashSet<int>> NodeSets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>*TRANSFORM</c> cards in deck order.</summary>
    public List<DeckTransform> Transforms { get; } = [];

    /// <summary>Steps in deck order, numbered from 1.</summary>
    public List<DeckStep> Steps { get; } = [];

    /// <summary>
    /// True when the result files are written on the original 1D/2D mesh:
    /// the last <c>OUTPUT=</c> of the first step's file cards says 2D (ccx
    /// keeps one such setting for every file card, fixed in the first step;
    /// the default is 3D).
    /// </summary>
    public bool OutputIs2D { get; set; }

    /// <summary>Element types the deck declares (normalized, e.g. "C3D8", "S8R").</summary>
    public HashSet<string> ElementTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Highest node id of a <c>*NODE</c> card; 0 when the deck defines no nodes.</summary>
    public int MaxNodeId { get; set; }

    /// <summary>Number of <c>*NODE</c> data lines.</summary>
    public int NodeCount { get; set; }

    /// <summary>True when the deck has elements ccx expands onto a 3D mesh.</summary>
    public bool HasExpandedElements => ElementTypes.Any(IsExpandedType);

    /// <summary>True when the deck has shell or beam elements.</summary>
    public bool HasShellOrBeamElements => ElementTypes.Any(IsShellOrBeamType);

    #endregion
}
