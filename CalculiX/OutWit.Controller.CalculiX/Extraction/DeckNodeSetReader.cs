using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Lexical node-set reader — just enough deck understanding for probes to map
/// a named set to node ids. No physics, no validation: sets the deck does not
/// define simply do not resolve, and their probes are skipped.
/// </summary>
public static class DeckNodeSetReader
{
    #region Functions

    /// <summary>
    /// Reads every node set of the deck: *NSET cards (GENERATE included) and
    /// sets declared inline as <c>*NODE, NSET=…</c> — the deck-side classifier
    /// offers those in the probe menu, so the node side must resolve them too.
    /// Keyed case-insensitively by set name.
    /// </summary>
    /// <param name="path">Path of the .inp deck.</param>
    /// <returns>Set name → node ids.</returns>
    public static Dictionary<string, HashSet<int>> Read(string path)
    {
        var sets = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        HashSet<int>? current = null;
        var generate = false;
        var nodeCard = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("**", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("*", StringComparison.Ordinal))
            {
                current = null;
                generate = false;
                nodeCard = false;

                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                var isNset = parts[0].Equals("*NSET", StringComparison.OrdinalIgnoreCase);
                var isNode = parts[0].Equals("*NODE", StringComparison.OrdinalIgnoreCase);
                if (!isNset && !isNode)
                    continue;

                nodeCard = isNode;
                foreach (var parameter in parts.Skip(1))
                {
                    if (parameter.StartsWith("NSET=", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = parameter[5..].Trim();
                        if (!sets.TryGetValue(name, out current))
                            sets[name] = current = [];
                    }
                    else if (parameter.Equals("GENERATE", StringComparison.OrdinalIgnoreCase))
                    {
                        generate = true;
                    }
                }

                continue;
            }

            if (current == null)
                continue;

            var tokens = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (nodeCard)
            {
                // A *NODE data line is "id, x, y, z" — only the leading token
                // is a node id; coordinates may look like integers and must
                // never be mistaken for members.
                if (tokens.Length > 0 && TryParse(tokens[0], out var node))
                    current.Add(node);
            }
            else if (generate)
            {
                if (tokens.Length >= 2
                    && TryParse(tokens[0], out var first)
                    && TryParse(tokens[1], out var last))
                {
                    var step = tokens.Length >= 3 && TryParse(tokens[2], out var s) && s > 0 ? s : 1;
                    for (var node = first; node <= last; node += step)
                        current.Add(node);
                }
            }
            else
            {
                foreach (var token in tokens)
                {
                    if (TryParse(token, out var node))
                        current.Add(node);
                }
            }
        }

        return sets;
    }

    private static bool TryParse(string token, out int value)
    {
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    #endregion
}
