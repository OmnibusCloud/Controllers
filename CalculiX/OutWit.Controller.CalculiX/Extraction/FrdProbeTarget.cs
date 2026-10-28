using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One probe as the accumulator evaluates it: the field it reads, the deck
/// nodes of its set, and the result nodes standing for each of them once
/// the .frd's geometry is known - the node itself where the file lists it,
/// the expanded nodes at or nearest to its position otherwise.
/// </summary>
internal sealed class FrdProbeTarget
{
    #region Constructors

    /// <summary>
    /// Creates the target.
    /// </summary>
    /// <param name="probe">The probe.</param>
    /// <param name="kind">The field the probe's quantity reads.</param>
    /// <param name="deckNodes">The nodes of the probe's set, as the deck numbers them.</param>
    public FrdProbeTarget(CcxProbeData probe, FrdFieldKind kind, HashSet<int> deckNodes)
    {
        Probe = probe;
        Kind = kind;
        DeckNodes = deckNodes;
    }

    #endregion

    #region Properties

    /// <summary>The probe.</summary>
    public CcxProbeData Probe { get; }

    /// <summary>The field the probe's quantity reads.</summary>
    public FrdFieldKind Kind { get; }

    /// <summary>The nodes of the probe's set, as the deck numbers them.</summary>
    public HashSet<int> DeckNodes { get; }

    /// <summary>Deck node → the result nodes standing for it.</summary>
    public Dictionary<int, int[]> Mapping { get; } = [];

    /// <summary>Deck nodes mapped to an expanded node at their own position.</summary>
    public int ByPosition { get; set; }

    /// <summary>Deck nodes mapped to their nearest expanded nodes.</summary>
    public int ByNearest { get; set; }

    /// <summary>Deck nodes with no result node at all.</summary>
    public int Unresolved { get; set; }

    #endregion
}
