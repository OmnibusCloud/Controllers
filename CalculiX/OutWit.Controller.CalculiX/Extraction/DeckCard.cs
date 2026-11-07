namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One keyword card of a deck, as ccx reads it: the keyword and its
/// parameters with every blank removed and upper-cased (<c>*Node Print,
/// NSET = Fix</c> is the card <c>NODEPRINT</c> with <c>NSET=FIX</c>), so a
/// card written with blanks around <c>=</c> or inside the keyword resolves the
/// same way it does in the solver.
/// </summary>
public sealed class DeckCard
{
    #region Constructors

    /// <summary>
    /// Creates a card.
    /// </summary>
    /// <param name="keyword">Normalized keyword without the leading asterisk, e.g. "NODEPRINT".</param>
    /// <param name="parameters">Normalized parameters in card order; a flag parameter has a null value.</param>
    /// <param name="lineIndex">Zero-based index of the keyword line in the deck.</param>
    public DeckCard(string keyword, IReadOnlyList<KeyValuePair<string, string?>> parameters, int lineIndex)
    {
        Keyword = keyword;
        Parameters = parameters;
        LineIndex = lineIndex;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Whether the card is the given keyword (normalized spelling, e.g. "ELFILE").
    /// </summary>
    /// <param name="keyword">Normalized keyword to compare with.</param>
    /// <returns>True for a match.</returns>
    public bool Is(string keyword)
    {
        return Keyword.Equals(keyword, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the card carries the parameter, as a flag or with a value.
    /// </summary>
    /// <param name="name">Normalized parameter name, e.g. "GENERATE".</param>
    /// <returns>True when present.</returns>
    public bool Has(string name)
    {
        return Parameters.Any(parameter => parameter.Key.Equals(name, StringComparison.Ordinal));
    }

    /// <summary>
    /// The value of a parameter; the last one wins when the card repeats it.
    /// </summary>
    /// <param name="name">Normalized parameter name, e.g. "NSET".</param>
    /// <returns>The value, or null when absent or given as a flag.</returns>
    public string? Value(string name)
    {
        string? value = null;
        foreach (var parameter in Parameters)
        {
            if (parameter.Key.Equals(name, StringComparison.Ordinal))
                value = parameter.Value;
        }

        return value;
    }

    public override string ToString()
    {
        return Parameters.Count == 0
            ? $"*{Keyword}"
            : $"*{Keyword},{string.Join(",", Parameters.Select(p => p.Value == null ? p.Key : $"{p.Key}={p.Value}"))}";
    }

    #endregion

    #region Properties

    /// <summary>Normalized keyword without the asterisk: blanks removed, upper-cased.</summary>
    public string Keyword { get; }

    /// <summary>Normalized parameters in card order; a flag parameter has a null value.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Parameters { get; }

    /// <summary>Zero-based index of the keyword line in the deck.</summary>
    public int LineIndex { get; }

    #endregion
}
