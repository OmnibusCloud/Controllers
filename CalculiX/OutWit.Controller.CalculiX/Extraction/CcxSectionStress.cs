namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The stress the extractor reports at the shell and beam nodes of a deck
/// that writes <c>OUTPUT=2D</c>, block by block of the .frd: at a shell node
/// the largest von Mises through the thickness - the node's mean stress from
/// the .frd plus and minus the bending stress recovered from the edge
/// section prints of the same increment (<see cref="ShellStressRecovery"/>);
/// at a beam node the section von Mises of the section forces ccx writes
/// there under <c>SECTION FORCES</c> (<see cref="DeckBeamSection"/>);
/// anywhere else the .frd's own stress. A block whose shells or beams cannot
/// be recovered carries the reason, and its von Mises columns are refused.
/// </summary>
internal sealed class CcxSectionStress
{
    #region Constants

    private const string SHELL_METHOD = "at the shell nodes, the largest of the top and bottom faces - the .frd's mean stress +/- 6M/t^2, M from a *SECTION PRINT of every element edge - and the mid-surface with 1.5 times the mean transverse shear";

    private const string BEAM_METHOD = "at the beam nodes, the largest over the section's corners, mid-sides and centre from N/A +/- M1/W1 +/- M2/W2 with the shear, from *EL FILE, SECTION FORCES";

    #endregion

    #region Fields

    private readonly DeckFacts m_facts;
    private readonly DeckStructure m_structure;
    private readonly CcxSectionStressPlan m_plan;
    private readonly DatResultFile? m_dat;
    private readonly HashSet<int> m_shellNodes = [];
    private readonly Dictionary<int, DeckBeamSection[]> m_beamNodes = [];
    private Dictionary<int, ShellBending>? m_shellValues;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the stress source of a deck.
    /// </summary>
    /// <param name="facts">The deck's facts.</param>
    /// <param name="structure">The deck's shells and beams.</param>
    /// <param name="plan">What can be recovered.</param>
    /// <param name="dat">The .dat of the solve; null when there is none.</param>
    public CcxSectionStress(DeckFacts facts, DeckStructure structure, CcxSectionStressPlan plan, DatResultFile? dat)
    {
        m_facts = facts;
        m_structure = structure;
        m_plan = plan;
        m_dat = dat;

        foreach (var element in structure.Shells.Values)
            m_shellNodes.UnionWith(element.Nodes);

        if (plan.RecoversBeams)
        {
            var sections = new Dictionary<int, HashSet<DeckBeamSection>>();
            foreach (var element in structure.Beams.Values)
            {
                foreach (var node in element.Nodes)
                {
                    if (!sections.TryGetValue(node, out var set))
                        sections[node] = set = [];

                    set.Add(structure.BeamSections[element.Id]);
                }
            }

            foreach (var (node, set) in sections)
                m_beamNodes[node] = set.ToArray();
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// A STRESS block begins: recovers its shells and finds whether its
    /// shell and beam stress can stand.
    /// </summary>
    /// <param name="header">The block's header.</param>
    /// <returns>Why the block's von Mises cannot be reported; null when it can.</returns>
    public string? BeginBlock(FrdBlockHeader header)
    {
        m_shellValues = null;
        var problem = m_plan.HasBeams ? BeamProblem() : null;
        if (problem == null && m_plan.HasShells)
            problem = ShellProblem(header);

        return problem;
    }

    /// <summary>
    /// The von Mises stress of a row of the current STRESS block.
    /// </summary>
    /// <param name="node">The row's node.</param>
    /// <param name="values">The row's six components.</param>
    /// <returns>The node's von Mises stress.</returns>
    public double VonMises(int node, double[] values)
    {
        if (m_shellValues != null && m_shellValues.TryGetValue(node, out var shell))
            return shell.VonMises(values);

        if (m_beamNodes.TryGetValue(node, out var sections) && !m_shellNodes.Contains(node))
            return sections.Max(section => section.VonMises(values));

        return FrdFieldStatistics.Scalar(FrdFieldKind.Stress, values);
    }

    /// <summary>
    /// How the von Mises of a block that stands was obtained, for an info note on its columns.
    /// </summary>
    /// <returns>The method's words; null when the deck has neither shells nor beams.</returns>
    public string? Method()
    {
        var methods = new List<string>();
        if (m_plan.HasShells)
            methods.Add(SHELL_METHOD);

        if (m_plan.HasBeams)
            methods.Add(BEAM_METHOD);

        return methods.Count == 0
            ? null
            : $"von Mises from section forces (OUTPUT=2D gives the mid-surface or beam-axis stress): {string.Join("; ", methods)}";
    }

    private string? BeamProblem()
    {
        if (m_plan.BeamProblem != null)
            return m_plan.BeamProblem;

        return m_structure.SectionForces
            ? null
            : "the deck's *EL FILE cards carry no SECTION FORCES (the controller adds it to step 1 before the solve)";
    }

    private string? ShellProblem(FrdBlockHeader header)
    {
        if (m_plan.ShellProblem != null)
            return m_plan.ShellProblem;

        if (m_structure.EdgeSurfaces.Count == 0)
            return "the deck carries no section prints of its shell edges (the controller adds them before the solve)";

        var step = m_facts.Steps.Find(candidate => candidate.Number == header.Step);
        if (step?.Procedure != "STATIC")
            return "the recovery runs on *STATIC steps only";

        var sections = m_dat?.Steps.Find(candidate => candidate.Number == header.Step)?.Sections;
        if (sections == null || sections.Count == 0)
            return "the .dat carries no section prints for the step";

        m_shellValues = ShellStressRecovery.Recover(m_structure, sections, header.Time, out var problem);
        return problem;
    }

    #endregion
}
