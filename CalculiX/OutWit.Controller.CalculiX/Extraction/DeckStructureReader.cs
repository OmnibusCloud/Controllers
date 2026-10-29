using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Reads the <see cref="DeckStructure"/> of a deck in one pass: the shell and
/// beam elements (a data line that does not carry all of an element's nodes
/// goes on on the next one, as ccx reads it), the element sets, the shell
/// and beam section cards, the material cards that are not linear elastic,
/// the deck's own section prints, the stress requests of the file cards and
/// the edge surfaces and <c>SECTION FORCES</c> the controller added.
/// </summary>
public static partial class DeckStructureReader
{
    #region Constants

    private static readonly HashSet<string> NONLINEAR_MATERIALS = new(StringComparer.Ordinal)
    {
        "PLASTIC", "DEFORMATIONPLASTICITY", "CREEP", "CYCLICHARDENING", "HYPERELASTIC",
        "HYPERFOAM", "USERMATERIAL", "VISCOPLASTIC"
    };

    private static readonly HashSet<string> ELEMENT_FILE_CARDS = new(StringComparer.Ordinal)
    {
        "ELFILE", "ELEMENTOUTPUT"
    };

    [GeneratedRegex(@"^S(\d)$")]
    private static partial Regex FaceRegex();

    // The other elements ccx maps onto the 1D/2D mesh: membranes, plane and
    // axisymmetric elements, trusses. Only their nodes matter here.
    [GeneratedRegex(@"^(?:M3D|CPS|CPE|CAX|T3D)(\d+)R?$")]
    private static partial Regex OtherMappedTypeRegex();

    #endregion

    #region Functions

    /// <summary>
    /// Reads the structure of a deck.
    /// </summary>
    /// <param name="path">Path of the .inp file.</param>
    /// <returns>The structure; a deck without shells or beams yields empty element tables.</returns>
    public static DeckStructure Read(string path)
    {
        return Read(File.ReadLines(path));
    }

    /// <summary>
    /// Reads the structure of deck text.
    /// </summary>
    /// <param name="lines">The deck, line by line.</param>
    /// <returns>The structure.</returns>
    public static DeckStructure Read(IEnumerable<string> lines)
    {
        var structure = new DeckStructure();
        var sets = new DeckElementSetCollector();
        var shellSections = new List<(string Set, double Thickness)>();
        var beamSections = new List<(string Set, DeckBeamSection Section)>();
        DeckCard? card = null;
        var step = 0;
        string? elementType = null;
        var otherNodes = 0;
        string? elementSet = null;
        var pending = new List<int>();
        var firstDataLine = false;
        string? surfaceName = null;

        foreach (var line in DeckCardReader.Read(lines))
        {
            if (line.IsKeyword)
            {
                card = line.Card;
                sets.Begin(card);
                firstDataLine = true;
                pending.Clear();
                elementType = null;
                otherNodes = 0;
                elementSet = null;
                surfaceName = null;

                if (card.Is("STEP"))
                    step++;
                else if (card.Is("ELEMENT"))
                {
                    var type = card.Value("TYPE");
                    elementType = type != null && DeckElement.NodeCountOf(type) > 0 ? type : null;
                    if (type != null && elementType == null && DeckFacts.IsShellOrBeamType(type))
                        structure.UnknownTypes.Add(type);
                    var other = type == null ? Match.Empty : OtherMappedTypeRegex().Match(type);
                    otherNodes = other.Success ? int.Parse(other.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    structure.HasTrusses |= other.Success && type!.StartsWith("T3D", StringComparison.Ordinal);
                    elementSet = card.Value("ELSET");
                }
                else if (NONLINEAR_MATERIALS.Contains(card.Keyword))
                    structure.NonlinearMaterial ??= $"*{card.Keyword}";
                else if (card.Is("SECTIONPRINT"))
                {
                    if (!CcxSectionStressPlan.IsEdgeSurface(card.Value("SURFACE")))
                        structure.OwnSectionPrints = true;
                }
                else if (card.Is("SURFACE"))
                {
                    var name = card.Value("NAME");
                    surfaceName = CcxSectionStressPlan.IsEdgeSurface(name) ? name : null;
                }
                else if (ELEMENT_FILE_CARDS.Contains(card.Keyword))
                {
                    structure.SectionForces |= step == 1 && card.Has("SECTIONFORCES");
                    structure.LocalElementOutput |= card.Value("GLOBAL") == "NO";
                }
                else if (card.Is("SHELLSECTION"))
                {
                    if (card.Has("COMPOSITE"))
                        structure.ShellSectionProblem ??= "a COMPOSITE shell section, whose stress is not linear through the thickness";
                    else if (card.Has("NODALTHICKNESS"))
                        structure.ShellSectionProblem ??= "a shell section with NODAL THICKNESS";
                }
                else if ((card.Is("BEAMSECTION") || card.Is("BEAMGENERALSECTION")) && card.Has("NODALTHICKNESS"))
                    structure.BeamSectionProblem ??= "a beam section with NODAL THICKNESS";

                continue;
            }

            var data = line.Data!;
            sets.Data(data);

            // Only shell and beam elements are kept: the sections the
            // recovery needs never cover anything else.
            if (card!.Is("ELEMENT"))
            {
                if (elementType != null)
                    ReadElement(data, elementType, elementSet, pending, structure, sets);
                else if (otherNodes > 0)
                    ReadOtherNodes(data, otherNodes, pending, structure);
            }
            else if (ELEMENT_FILE_CARDS.Contains(card.Keyword) || card.Is("NODEFILE") || card.Is("NODEOUTPUT"))
            {
                if (data.Split(',', StringSplitOptions.TrimEntries).Any(token => token.Equals("S", StringComparison.OrdinalIgnoreCase)))
                    structure.StressRequested = true;
            }
            else if (card.Is("SURFACE") && surfaceName != null)
            {
                var tokens = data.Split(',', StringSplitOptions.TrimEntries);
                var face = tokens.Length >= 2 ? FaceRegex().Match(tokens[1].ToUpperInvariant()) : Match.Empty;
                if (face.Success && int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var element))
                    structure.EdgeSurfaces[surfaceName] = (element, int.Parse(face.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            else if (firstDataLine && card.Is("SHELLSECTION") && !card.Has("COMPOSITE") && card.Value("ELSET") is { } shellSet)
            {
                if (TryNumbers(data, 1, out var thickness))
                    shellSections.Add((shellSet, thickness[0]));
            }
            else if (firstDataLine && (card.Is("BEAMSECTION") || card.Is("BEAMGENERALSECTION")) && card.Value("ELSET") is { } beamSet)
            {
                var kind = card.Value("SECTION") ?? string.Empty;
                var count = kind == "BOX" ? 6 : 2;
                if (TryNumbers(data, count, out var dimensions))
                    beamSections.Add((beamSet, new DeckBeamSection(kind, dimensions)));
            }

            firstDataLine = false;
        }

        // Whether beam elements meet in line needs their nodes' positions: a
        // second pass, for the beam nodes only.
        if (structure.Beams.Count > 0)
        {
            var beamNodes = structure.Beams.Values.SelectMany(element => element.Nodes).ToHashSet();
            foreach (var (node, point) in DeckFactsReader.ReadCoordinates(lines, beamNodes))
                structure.BeamNodeCoordinates[node] = new Vector3D(point[0], point[1], point[2]);
        }

        var resolved = sets.Finish();
        foreach (var (set, thickness) in shellSections)
        {
            foreach (var element in Members(resolved, set).Where(structure.Shells.ContainsKey))
                structure.ShellThickness[element] = thickness;
        }

        foreach (var (set, section) in beamSections)
        {
            foreach (var element in Members(resolved, set).Where(structure.Beams.ContainsKey))
                structure.BeamSections[element] = section;
        }

        return structure;
    }

    private static void ReadElement(string data, string type, string? setName, List<int> pending, DeckStructure structure, DeckElementSetCollector sets)
    {
        foreach (var token in data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                pending.Add(value);
        }

        var count = DeckElement.NodeCountOf(type);
        if (pending.Count < count + 1)
            return;

        var element = new DeckElement(pending[0], type, pending.GetRange(1, count).ToArray());
        if (element.IsShell)
            structure.Shells[element.Id] = element;
        else
            structure.Beams[element.Id] = element;

        if (setName != null)
            sets.AddElement(setName, element.Id);

        pending.Clear();
    }

    private static void ReadOtherNodes(string data, int count, List<int> pending, DeckStructure structure)
    {
        foreach (var token in data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                pending.Add(value);
        }

        if (pending.Count < count + 1)
            return;

        structure.OtherMappedNodes.UnionWith(pending.GetRange(1, count));
        pending.Clear();
    }

    private static IEnumerable<int> Members(Dictionary<string, HashSet<int>> sets, string name)
    {
        return sets.TryGetValue(name, out var members) ? members : [];
    }

    private static bool TryNumbers(string data, int count, out double[] numbers)
    {
        var tokens = data.Split(',', StringSplitOptions.TrimEntries);
        numbers = new double[count];
        if (tokens.Length < count)
            return false;

        for (var i = 0; i < count; i++)
        {
            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        return true;
    }

    #endregion
}
