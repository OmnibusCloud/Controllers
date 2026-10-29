using System.Text.RegularExpressions;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Whether the stress of a deck's shells and beams can be recovered from
/// section forces when the deck writes <c>OUTPUT=2D</c>, where ccx maps it
/// onto the mid-surface or the beam axis and the bending stress is gone (an
/// S8R cantilever read 0.81 against 75 analytic, a B32 4.75 against 60).
/// ccx 2.22 has no field of shell section forces (<c>*EL FILE, SECTION
/// FORCES</c> changes the output of beams only, map3dto1d2d.f; SNEG and
/// SPOS fill only the US3/US45 user shells), so:
/// <list type="bullet">
/// <item>beams: <c>*EL FILE, SECTION FORCES</c> makes ccx write the section
/// forces and moments at the beam nodes of the .frd in place of the stress;</item>
/// <item>shells: a <c>*SECTION PRINT</c> on a surface made of one element edge
/// prints the force and moment over that cut (printoutface.f), so one print
/// per edge gives the membrane force and bending moment of every edge.</item>
/// </list>
/// The plan is made twice from the same cards: before the solve, to add the
/// output, and in the extraction, from the deck that was solved.
/// </summary>
public sealed partial class CcxSectionStressPlan
{
    #region Constants

    /// <summary>The name prefix of the controller's edge surfaces: WITEDGE1, WITEDGE2, …</summary>
    public const string EDGE_SURFACE_PREFIX = "WITEDGE";

    /// <summary>The name prefix of the controller's section prints: WITPRINT1, WITPRINT2, …</summary>
    public const string EDGE_PRINT_PREFIX = "WITPRINT";

    /// <summary>
    /// The most shell edges the recovery prints. ccx checks every new print
    /// request against all earlier ones (sectionprints.f), so the cost grows
    /// with the square of the count: a 10 000-element S4 plate (20 250 edges)
    /// solved in 3.1 s against 1.3 s, a 40 000-element one (80 500 edges)
    /// in 26.5 s against 5.3 s (ccx 2.22, 4 threads, 2026-10-29).
    /// </summary>
    public const int MAX_SHELL_EDGES = 20_000;

    // ccx integrates the edge faces of the expanded S3 (C3D6) and S4R
    // (C3D8R) at one point, at the face centre, so their prints carry no
    // moment (measured: exactly 0); its beam section forces of B31R (C3D8R)
    // carry no moment for the same reason.
    private static readonly HashSet<string> SHELL_TYPES = new(StringComparer.Ordinal) { "S4", "S6", "S8", "S8R" };

    private static readonly HashSet<string> BEAM_TYPES = new(StringComparer.Ordinal) { "B31", "B32", "B32R" };

    // Beam elements meeting at a node at less than this angle are taken as
    // one beam line: ccx averages the section forces of the elements on a
    // node, each in the element's own frame, and frames 5 degrees apart mix
    // the components by sin 5 degrees = 9 % at most.
    private static readonly double IN_LINE_COSINE = System.Math.Cos(5.0 * System.Math.PI / 180.0);

    [GeneratedRegex(@"^WITEDGE\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EdgeSurfaceRegex();

    #endregion

    #region Constructors

    private CcxSectionStressPlan()
    {
    }

    #endregion

    #region Functions

    /// <summary>
    /// Whether a surface name is one of the controller's edge surfaces.
    /// </summary>
    /// <param name="name">The surface name; null for none.</param>
    /// <returns>True for WITEDGE followed by digits.</returns>
    public static bool IsEdgeSurface(string? name)
    {
        return name != null && EdgeSurfaceRegex().IsMatch(name);
    }

    /// <summary>
    /// Makes the plan of a deck.
    /// </summary>
    /// <param name="facts">The deck's facts.</param>
    /// <param name="structure">The deck's shells and beams.</param>
    /// <returns>The plan; nothing applies to a deck that writes the expanded 3D mesh.</returns>
    public static CcxSectionStressPlan For(DeckFacts facts, DeckStructure structure)
    {
        var plan = new CcxSectionStressPlan
        {
            HasShells = structure.Shells.Count > 0,
            HasBeams = structure.Beams.Count > 0,
            Applies = facts.OutputIs2D && structure.StressRequested
        };

        if (!plan.Applies)
            return plan;

        // A type the element tables do not hold would pass its mid-surface
        // stress for the answer: nothing is recovered.
        if (structure.UnknownTypes.Count > 0)
        {
            plan.ShellProblem = plan.BeamProblem = $"the deck has {string.Join(" and ", structure.UnknownTypes)} elements, which the recovery does not know";
            plan.HasShells |= structure.UnknownTypes.Any(type => type.StartsWith('S'));
            plan.HasBeams |= structure.UnknownTypes.Any(type => type.StartsWith('B'));
            return plan;
        }

        if (plan.HasShells)
            plan.ShellProblem = FindShellProblem(facts, structure, plan.ShellEdges);

        if (plan.HasBeams)
            plan.BeamProblem = FindBeamProblem(structure);

        if (plan.ShellProblem != null)
            plan.ShellEdges.Clear();

        return plan;
    }

    private static string? FindShellProblem(DeckFacts facts, DeckStructure structure, List<(int Element, int Face)> edges)
    {
        if (structure.NonlinearMaterial != null)
            return $"the deck's material is not linear elastic ({structure.NonlinearMaterial}), so the stress is not linear through the thickness";

        var unsupported = structure.Shells.Values.Select(element => element.Type).Where(type => !SHELL_TYPES.Contains(type)).Distinct().Order().ToList();
        if (unsupported.Count > 0)
            return $"the deck has {string.Join(" and ", unsupported)} elements, whose edge faces ccx integrates at one point, so their section prints carry no moment";

        if (structure.ShellSectionProblem != null)
            return $"the deck has {structure.ShellSectionProblem}";

        if (structure.Shells.Keys.Any(element => !structure.ShellThickness.ContainsKey(element)))
            return "the deck has shell elements without the thickness of a *SHELL SECTION";

        if (structure.LocalElementOutput)
            return "the deck writes the element output in local frames (GLOBAL=NO), and the section prints are global";

        if (structure.OwnSectionPrints)
            return "the deck has *SECTION PRINT cards of its own, which the controller's prints would replace";

        if (!facts.Steps.Any(step => step.Procedure == "STATIC"))
            return "the recovery runs on *STATIC steps only";

        foreach (var owners in structure.ShellEdges().Values)
            edges.Add((owners[0].Element.Id, owners[0].Edge + 3));

        edges.Sort();

        return edges.Count > MAX_SHELL_EDGES
            ? $"the deck has {edges.Count} shell edges, more than the {MAX_SHELL_EDGES} the recovery prints (ccx reads print requests in quadratic time)"
            : null;
    }

    private static string? FindBeamProblem(DeckStructure structure)
    {
        if (structure.NonlinearMaterial != null)
            return $"the deck's material is not linear elastic ({structure.NonlinearMaterial}), so the stress is not linear across the section";

        var unsupported = structure.Beams.Values.Select(element => element.Type).Where(type => !BEAM_TYPES.Contains(type)).Distinct().Order().ToList();
        if (unsupported.Count > 0)
            return $"the deck has {string.Join(" and ", unsupported)} elements, whose section ccx integrates at one point, so their section forces carry no moment";

        if (structure.BeamSectionProblem != null)
            return $"the deck has {structure.BeamSectionProblem}";

        if (structure.Beams.Keys.Any(element => !structure.BeamSections.ContainsKey(element)))
            return "the deck has beam elements without a *BEAM SECTION or *BEAM GENERAL SECTION";

        var section = structure.BeamSections.Values.Select(candidate => candidate.Unsupported()).FirstOrDefault(problem => problem != null);
        if (section != null)
            return $"the deck has {section}";

        // ccx expands trusses as beams: SECTION FORCES would turn their
        // stress into forces too.
        if (structure.HasTrusses)
            return "the deck has truss elements, whose stress SECTION FORCES would turn into forces as well";

        return FindNodeProblem(structure);
    }

    // ccx writes one value per node, the mean over the elements on it: a
    // beam's section forces averaged with a shell's stress, or with another
    // beam's forces in another frame, are no longer either.
    private static string? FindNodeProblem(DeckStructure structure)
    {
        var incidence = new Dictionary<int, List<DeckElement>>();
        foreach (var element in structure.Beams.Values)
        {
            foreach (var node in element.Nodes)
            {
                if (!incidence.TryGetValue(node, out var elements))
                    incidence[node] = elements = [];

                elements.Add(element);
            }
        }

        var shellNodes = structure.Shells.Values.SelectMany(element => element.Nodes).ToHashSet();
        var shared = incidence.Keys.Count(node => shellNodes.Contains(node) || structure.OtherMappedNodes.Contains(node));
        if (shared > 0)
            return $"{shared} beam nodes are nodes of shell, membrane, plane or truss elements too, where ccx averages the beam's section forces with their stress";

        var joints = incidence.Count(pair => !InLine(pair.Key, pair.Value, structure));
        return joints > 0
            ? $"{joints} beam nodes join beam elements at an angle, against each other's direction or of different sections, where ccx averages their section forces in different frames"
            : null;
    }

    private static bool InLine(int node, List<DeckElement> elements, DeckStructure structure)
    {
        if (elements.Count == 1)
            return true;

        if (elements.Count > 2)
            return false;

        var (first, second) = (elements[0], elements[1]);
        if (!ReferenceEquals(structure.BeamSections[first.Id], structure.BeamSections[second.Id]))
            return false;

        // One element ends where the other one starts.
        var (from, to) = first.Nodes[^1] == node && second.Nodes[0] == node ? (first, second)
            : second.Nodes[^1] == node && first.Nodes[0] == node ? (second, first)
            : (null, null);
        if (from == null || to == null)
            return false;

        var (a, b) = (Chord(from, structure), Chord(to, structure));
        return a is { } incoming && b is { } outgoing && incoming.Dot(outgoing) >= IN_LINE_COSINE;
    }

    private static Vector3D? Chord(DeckElement element, DeckStructure structure)
    {
        return structure.BeamNodeCoordinates.TryGetValue(element.Nodes[0], out var start)
               && structure.BeamNodeCoordinates.TryGetValue(element.Nodes[^1], out var end)
               && (end - start).Length > 0
            ? (end - start).Unit()
            : null;
    }

    #endregion

    #region Properties

    /// <summary>
    /// True when the deck writes the 1D/2D mesh (<c>OUTPUT=2D</c>) and asks
    /// for the stress: the only decks whose shell and beam stress needs recovering.
    /// </summary>
    public bool Applies { get; private init; }

    /// <summary>True when the deck has shell elements.</summary>
    public bool HasShells { get; private set; }

    /// <summary>True when the deck has beam elements.</summary>
    public bool HasBeams { get; private set; }

    /// <summary>
    /// Why the shell stress cannot be recovered, as words for a note; null
    /// when it can, when the deck has no shells or writes the expanded mesh.
    /// </summary>
    public string? ShellProblem { get; private set; }

    /// <summary>Why the beam stress cannot be recovered; null when it can or there is nothing to recover.</summary>
    public string? BeamProblem { get; private set; }

    /// <summary>
    /// The shell edges to print, one per edge shared by any number of
    /// elements: the element with the lowest id on the edge and the edge's
    /// ccx face number. Empty when the shell recovery does not apply.
    /// </summary>
    public List<(int Element, int Face)> ShellEdges { get; } = [];

    /// <summary>True when the shell recovery applies: the deck needs one print per edge.</summary>
    public bool RecoversShells => Applies && HasShells && ShellProblem == null && ShellEdges.Count > 0;

    /// <summary>True when the beam recovery applies: the deck needs <c>SECTION FORCES</c>.</summary>
    public bool RecoversBeams => Applies && HasBeams && BeamProblem == null;

    /// <summary>
    /// True when the stress of every shell and beam can be recovered: the von
    /// Mises columns cover the whole model, so one family that cannot be
    /// recovered leaves the other's output out of the deck too.
    /// </summary>
    public bool RecoversAll => Applies && (HasShells || HasBeams) && (!HasShells || RecoversShells) && (!HasBeams || RecoversBeams);

    #endregion
}
