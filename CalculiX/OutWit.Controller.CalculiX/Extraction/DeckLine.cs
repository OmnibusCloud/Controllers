namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One line of a deck as the card reader hands it out: the card it belongs
/// to, and the data line itself for a data line (null on the card's own
/// keyword line).
/// </summary>
public readonly struct DeckLine
{
    #region Constructors

    /// <summary>
    /// Creates a line.
    /// </summary>
    /// <param name="card">The card the line belongs to.</param>
    /// <param name="data">The trimmed data line; null for the keyword line itself.</param>
    public DeckLine(DeckCard card, string? data)
    {
        Card = card;
        Data = data;
    }

    #endregion

    #region Properties

    /// <summary>The card the line belongs to.</summary>
    public DeckCard Card { get; }

    /// <summary>The trimmed data line; null for the keyword line itself.</summary>
    public string? Data { get; }

    /// <summary>True on the card's keyword line.</summary>
    public bool IsKeyword => Data == null;

    #endregion
}
