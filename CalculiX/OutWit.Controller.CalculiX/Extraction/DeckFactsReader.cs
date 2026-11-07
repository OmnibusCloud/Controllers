using System.Globalization;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Reads the <see cref="DeckFacts"/> of a deck in one pass, and the
/// coordinates of named nodes in a second one when the extractor needs
/// them (a cylindrical <c>*TRANSFORM</c>, a probe on an expanded mesh) -
/// a deck of a million nodes is not held in memory on the chance.
/// </summary>
public static class DeckFactsReader
{
    #region Constants

    private static readonly HashSet<string> PROCEDURES = new(StringComparer.Ordinal)
    {
        "STATIC", "FREQUENCY", "BUCKLE", "COMPLEXFREQUENCY", "HEATTRANSFER", "DYNAMIC",
        "MODALDYNAMIC", "STEADYSTATEDYNAMICS", "COUPLEDTEMPERATURE-DISPLACEMENT",
        "UNCOUPLEDTEMPERATURE-DISPLACEMENT", "VISCO", "CFD", "ELECTROMAGNETICS", "GREEN",
        "SENSITIVITY", "SUBSTRUCTUREGENERATE", "ROBUSTDESIGN", "FEASIBLEDIRECTION",
        "CRACKPROPAGATION", "HCF", "NOANALYSIS"
    };

    private static readonly HashSet<string> FILE_CARDS = new(StringComparer.Ordinal)
    {
        "NODEFILE", "ELFILE", "CONTACTFILE", "NODEOUTPUT", "ELEMENTOUTPUT", "CONTACTOUTPUT"
    };

    #endregion

    #region Functions

    /// <summary>
    /// Reads the facts of a deck.
    /// </summary>
    /// <param name="path">Path of the .inp file.</param>
    /// <returns>The facts; a deck without cards yields empty facts.</returns>
    public static DeckFacts Read(string path)
    {
        return Read(File.ReadLines(path));
    }

    /// <summary>
    /// Reads the facts of deck text.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <returns>The facts.</returns>
    public static DeckFacts Read(IEnumerable<string> lines)
    {
        var facts = new DeckFacts();
        var sets = new DeckNodeSetCollector();
        DeckCard? card = null;
        DeckStep? step = null;
        double[]? transformPoints = null;
        string? transformSet = null;
        var transformCylindrical = false;

        foreach (var line in DeckCardReader.Read(lines))
        {
            if (line.IsKeyword)
            {
                card = line.Card;
                sets.Begin(card);
                transformPoints = null;

                if (card.Is("STEP"))
                {
                    step = new DeckStep(facts.Steps.Count + 1, null);
                    facts.Steps.Add(step);
                }
                else if (card.Is("ENDSTEP"))
                {
                    step = null;
                }
                else if (step != null && step.Procedure == null && PROCEDURES.Contains(card.Keyword))
                {
                    facts.Steps[^1] = step = new DeckStep(step.Number, card.Keyword);
                }
                else if (card.Is("ELEMENT"))
                {
                    var type = card.Value("TYPE");
                    if (!string.IsNullOrEmpty(type))
                        facts.ElementTypes.Add(type);
                }
                else if (card.Is("TRANSFORM"))
                {
                    transformSet = card.Value("NSET");
                    transformCylindrical = card.Value("TYPE")?.StartsWith('C') == true;
                    transformPoints = transformSet == null ? null : new double[6];
                }
                else if (FILE_CARDS.Contains(card.Keyword) && facts.Steps.Count == 1)
                {
                    // ccx keeps one OUTPUT= for every file card and only the
                    // first step can set it; the last card of that step wins.
                    var output = card.Value("OUTPUT");
                    if (output != null)
                        facts.OutputIs2D = output.StartsWith("2D", StringComparison.Ordinal);
                }

                continue;
            }

            var data = line.Data!;
            sets.Data(data);

            if (card!.Is("NODE"))
            {
                var comma = data.IndexOf(',');
                var id = comma < 0 ? data : data[..comma];
                if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var node))
                {
                    facts.NodeCount++;
                    if (node > facts.MaxNodeId)
                        facts.MaxNodeId = node;
                }
            }
            else if (transformPoints != null && transformSet != null)
            {
                // The first data line carries the six numbers; a short or
                // unreadable line leaves the card out, as ccx would refuse it.
                var tokens = data.Split(',', StringSplitOptions.TrimEntries);
                var complete = tokens.Length >= 6;
                for (var i = 0; complete && i < 6; i++)
                    complete = double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out transformPoints[i]);

                if (complete)
                    facts.Transforms.Add(new DeckTransform(transformSet, transformCylindrical, transformPoints));

                transformPoints = null;
            }
        }

        foreach (var set in sets.Finish())
            facts.NodeSets[set.Key] = set.Value;

        return facts;
    }

    /// <summary>
    /// Reads the coordinates of the given nodes from the deck's <c>*NODE</c> cards.
    /// </summary>
    /// <param name="path">Path of the .inp file.</param>
    /// <param name="nodes">The node ids wanted.</param>
    /// <returns>Node id → x, y, z for every wanted node the deck defines (a missing z reads as 0).</returns>
    public static Dictionary<int, double[]> ReadCoordinates(string path, IReadOnlySet<int> nodes)
    {
        return ReadCoordinates(File.ReadLines(path), nodes);
    }

    /// <summary>
    /// Reads the coordinates of the given nodes from deck text.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <param name="nodes">The node ids wanted.</param>
    /// <returns>Node id → x, y, z for every wanted node the deck defines (a missing z reads as 0).</returns>
    public static Dictionary<int, double[]> ReadCoordinates(IEnumerable<string> lines, IReadOnlySet<int> nodes)
    {
        var coordinates = new Dictionary<int, double[]>();
        if (nodes.Count == 0)
            return coordinates;

        foreach (var line in DeckCardReader.Read(lines))
        {
            if (line.IsKeyword || !line.Card.Is("NODE"))
                continue;

            var tokens = line.Data!.Split(',', StringSplitOptions.TrimEntries);
            if (tokens.Length < 2 || !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var node) || !nodes.Contains(node))
                continue;

            var point = new double[3];
            for (var i = 0; i < 3 && i + 1 < tokens.Length; i++)
                double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out point[i]);

            coordinates[node] = point;
        }

        return coordinates;
    }

    #endregion
}
