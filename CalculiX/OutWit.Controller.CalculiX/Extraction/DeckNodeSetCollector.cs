using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Collects the node sets of a deck from the cards the reader hands it:
/// <c>*NSET</c> lists (GENERATE included, previously defined set names among
/// the ids expanded to any depth) and sets declared inline as <c>*NODE,
/// NSET=…</c>. A set whose reference never resolves - an unknown name or a
/// cycle - is dropped whole, transitively: a probe over a partial set would
/// return a plausible wrong number, and loud-missing beats silent-wrong.
/// </summary>
internal sealed class DeckNodeSetCollector
{
    #region Fields

    private readonly Dictionary<string, HashSet<int>> m_sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> m_references = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<int>? m_current;
    private string? m_currentName;
    private bool m_generate;
    private bool m_nodeCard;

    #endregion

    #region Functions

    /// <summary>
    /// Starts a card; only <c>*NSET</c> and <c>*NODE</c> cards with a set name open a set.
    /// </summary>
    /// <param name="card">The card.</param>
    public void Begin(DeckCard card)
    {
        m_current = null;
        m_currentName = null;
        m_generate = false;
        m_nodeCard = card.Is("NODE");

        if (!card.Is("NSET") && !m_nodeCard)
            return;

        var name = card.Value("NSET");
        if (string.IsNullOrEmpty(name))
            return;

        m_currentName = name;
        if (!m_sets.TryGetValue(name, out m_current))
            m_sets[name] = m_current = [];

        m_generate = card.Has("GENERATE");
    }

    /// <summary>
    /// Takes a data line of the current card.
    /// </summary>
    /// <param name="line">The trimmed data line.</param>
    public void Data(string line)
    {
        if (m_current == null || m_currentName == null)
            return;

        var tokens = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (m_nodeCard)
        {
            // A *NODE data line is "id, x, y, z" - only the leading token is
            // a node id; coordinates may look like integers and must never be
            // mistaken for members.
            if (tokens.Length > 0 && TryParse(tokens[0], out var node))
                m_current.Add(node);
        }
        else if (m_generate)
        {
            if (tokens.Length >= 2 && TryParse(tokens[0], out var first) && TryParse(tokens[1], out var last))
            {
                var step = tokens.Length >= 3 && TryParse(tokens[2], out var s) && s > 0 ? s : 1;
                for (var node = first; node <= last; node += step)
                    m_current.Add(node);
            }
        }
        else
        {
            foreach (var token in tokens)
            {
                if (TryParse(token, out var node))
                    m_current.Add(node);
                else
                    Reference(m_currentName, token);
            }
        }
    }

    /// <summary>
    /// Resolves the set references and returns the sets.
    /// </summary>
    /// <returns>Set name → node ids, keyed case-insensitively.</returns>
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
