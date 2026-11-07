namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Reads a deck the way ccx does, one line at a time: a keyword line (with
/// its continuation lines folded in - a keyword line ending in a comma goes
/// on on the next line) opens a card, every following line up to the next
/// keyword is a data line of that card, comment lines (<c>**</c>) and blank
/// lines are skipped. Keyword lines lose every blank and are upper-cased
/// before they are split, as in the solver's own reader: <c>*NSET, NSET =
/// TIP</c> is the same card as <c>*NSET,NSET=TIP</c>. Data lines are only
/// trimmed. No include files are followed: the decks the controller solves
/// are baked flat.
/// </summary>
public static class DeckCardReader
{
    #region Functions

    /// <summary>
    /// Reads the deck.
    /// </summary>
    /// <param name="path">Path of the .inp file.</param>
    /// <returns>The lines, in deck order; a data line before any card is skipped.</returns>
    public static IEnumerable<DeckLine> Read(string path)
    {
        return Read(File.ReadLines(path));
    }

    /// <summary>
    /// Reads deck text.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <returns>The lines, in deck order; a data line before any card is skipped.</returns>
    public static IEnumerable<DeckLine> Read(IEnumerable<string> lines)
    {
        DeckCard? card = null;
        string? pendingKeyword = null;
        var pendingIndex = 0;
        var index = -1;

        foreach (var rawLine in lines)
        {
            index++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("**", StringComparison.Ordinal))
                continue;

            if (pendingKeyword != null)
            {
                pendingKeyword += line;
                if (line.EndsWith(','))
                    continue;

                card = Parse(pendingKeyword, pendingIndex);
                pendingKeyword = null;
                yield return new DeckLine(card, null);
                continue;
            }

            if (line.StartsWith('*'))
            {
                if (line.EndsWith(','))
                {
                    pendingKeyword = line;
                    pendingIndex = index;
                    continue;
                }

                card = Parse(line, index);
                yield return new DeckLine(card, null);
                continue;
            }

            if (card != null)
                yield return new DeckLine(card, line);
        }

        // A deck ending in an unfinished keyword line: ccx reports an
        // incomplete card; here the card is handed out as it stands.
        if (pendingKeyword != null)
            yield return new DeckLine(Parse(pendingKeyword, pendingIndex), null);
    }

    /// <summary>
    /// Normalizes a keyword line the way ccx does: every blank (space or tab)
    /// removed, upper-cased.
    /// </summary>
    /// <param name="line">The keyword line, with or without its asterisk.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string line)
    {
        var buffer = new char[line.Length];
        var length = 0;

        foreach (var character in line)
        {
            if (character == ' ' || character == '\t' || character == '\r')
                continue;

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer, 0, length);
    }

    private static DeckCard Parse(string line, int index)
    {
        var parts = Normalize(line).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var keyword = parts.Length > 0 ? parts[0].TrimStart('*') : string.Empty;
        var parameters = new List<KeyValuePair<string, string?>>(parts.Length - 1);

        for (var i = 1; i < parts.Length; i++)
        {
            var equals = parts[i].IndexOf('=');
            parameters.Add(equals < 0
                ? new KeyValuePair<string, string?>(parts[i], null)
                : new KeyValuePair<string, string?>(parts[i][..equals], parts[i][(equals + 1)..]));
        }

        return new DeckCard(keyword, parameters, index);
    }

    #endregion
}
