using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Collects the element sets of a deck from the cards the reader hands it:
/// <c>*ELSET</c> lists (GENERATE included, previously defined set names among
/// the ids expanded to any depth) and the sets declared inline as
/// <c>*ELEMENT, ELSET=…</c>. A set whose reference never resolves - an
/// unknown name or a cycle - is dropped whole, as the node sets are.
/// </summary>
internal sealed class DeckElementSetCollector
{
    #region Fields

    private readonly Dictionary<string, HashSet<int>> m_sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> m_references = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<int>? m_current;
    private string? m_currentName;
    private bool m_generate;

    #endregion

    #region Functions

    /// <summary>
    /// Starts a card; only <c>*ELSET</c> cards open a set here - the
    /// elements of an <c>*ELEMENT, ELSET=</c> card come through <see cref="AddElement"/>.
    /// </summary>
    /// <param name="card">The card.</param>
    public void Begin(DeckCard card)
    {
        m_current = null;
        m_currentName = null;
        m_generate = false;

        if (!card.Is("ELSET"))
            return;

        var name = card.Value("ELSET");
        if (string.IsNullOrEmpty(name))
            return;

        m_currentName = name;
        m_current = Set(name);
        m_generate = card.Has("GENERATE");
    }

    /// <summary>
    /// Takes a data line of the current <c>*ELSET</c> card.
    /// </summary>
    /// <param name="line">The trimmed data line.</param>
    public void Data(string line)
    {
        if (m_current == null || m_currentName == null)
            return;

        var tokens = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (m_generate)
        {
            if (tokens.Length >= 2 && TryParse(tokens[0], out var first) && TryParse(tokens[1], out var last))
            {
                var step = tokens.Length >= 3 && TryParse(tokens[2], out var s) && s > 0 ? s : 1;
                for (var element = first; element <= last; element += step)
                    m_current.Add(element);
            }

            return;
        }

        foreach (var token in tokens)
        {
            if (TryParse(token, out var element))
                m_current.Add(element);
            else
                Reference(m_currentName, token);
        }
    }

    /// <summary>
    /// Adds an element of an <c>*ELEMENT, ELSET=</c> card to its set.
    /// </summary>
    /// <param name="setName">The set the card names.</param>
    /// <param name="element">The element id.</param>
    public void AddElement(string setName, int element)
    {
        Set(setName).Add(element);
    }

    /// <summary>
    /// Resolves the set references and returns the sets.
    /// </summary>
    /// <returns>Set name → element ids, keyed case-insensitively.</returns>
    public Dictionary<string, HashSet<int>> Finish()
    {
        var pending = new HashSet<string>(m_references.Keys, StringComparer.OrdinalIgnoreCase);

        var progressed = true;
        while (progressed && pending.Count > 0)
        {
            progressed = false;
            foreach (var name in pending.ToList())
            {
                var references = m_references[name];
                if (references.Any(pending.Contains) || !references.All(m_sets.ContainsKey))
                    continue;

                foreach (var reference in references)
                    m_sets[name].UnionWith(m_sets[reference]);

                pending.Remove(name);
                progressed = true;
            }
        }

        foreach (var unresolved in pending)
            m_sets.Remove(unresolved);

        return m_sets;
    }

    private HashSet<int> Set(string name)
    {
        if (!m_sets.TryGetValue(name, out var set))
            m_sets[name] = set = [];

        return set;
    }

    private void Reference(string setName, string token)
    {
        if (!m_references.TryGetValue(setName, out var references))
            m_references[setName] = references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        references.Add(token);
    }

    private static bool TryParse(string token, out int value)
    {
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    #endregion
}
