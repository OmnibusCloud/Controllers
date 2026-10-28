namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Lexical node-set reader — just enough deck understanding for probes to map
/// a named set to node ids. No physics, no validation: sets the deck does not
/// define simply do not resolve, and their probes are skipped. Cards are read
/// as ccx reads them (<see cref="DeckCardReader"/>): <c>*NSET, NSET = TIP</c>
/// with blanks around the equals sign is the set TIP, not a card without a
/// name.
/// </summary>
public static class DeckNodeSetReader
{
    #region Functions

    /// <summary>
    /// Reads every node set of the deck: *NSET cards (GENERATE included,
    /// previously defined set names among the ids expanded) and sets declared
    /// inline as <c>*NODE, NSET=…</c> — the deck-side classifier offers those
    /// in the probe menu, so the node side must resolve them too. Keyed
    /// case-insensitively by set name.
    /// </summary>
    /// <param name="path">Path of the .inp deck.</param>
    /// <returns>Set name → node ids.</returns>
    public static Dictionary<string, HashSet<int>> Read(string path)
    {
        return Read(File.ReadLines(path));
    }

    /// <summary>
    /// Reads every node set of deck text.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <returns>Set name → node ids.</returns>
    public static Dictionary<string, HashSet<int>> Read(IEnumerable<string> lines)
    {
        var collector = new DeckNodeSetCollector();

        foreach (var line in DeckCardReader.Read(lines))
        {
            if (line.IsKeyword)
                collector.Begin(line.Card);
            else
                collector.Data(line.Data!);
        }

        return collector.Finish();
    }

    #endregion
}
