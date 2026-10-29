using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The extractor's .frd visitor: reduces every block to running figures as
/// it streams by, per step and field - the last block of a field in a step
/// is that step's final state, the peak over a step's temperature blocks is
/// the transient's history - and samples the nodes the probes ask for.
/// A block with a value that is not a number empties its family for the
/// step, with a note naming the node, rather than reporting NaN or zero.
/// The stress of the shell and beam nodes of an OUTPUT=2D deck comes from
/// <see cref="CcxSectionStress"/>, or the block's von Mises is refused.
/// </summary>
internal sealed class FrdResponseAccumulator : IFrdResultVisitor
{
    #region Constants

    private const string DATASET_TEMPERATURE = "NDTEMP";

    private const string DATASET_DISPLACEMENT = "DISP";

    private const string DATASET_STRESS = "STRESS";

    #endregion

    #region Fields

    private readonly DeckFacts? m_facts;
    private readonly string? m_deckPath;
    private readonly CcxResponseSink m_sink;
    private readonly bool m_refuseVonMises;
    private readonly CcxSectionStress? m_sectionStress;
    private readonly List<FrdProbeTarget> m_targets = [];
    private readonly HashSet<int> m_probeNodes = [];
    private readonly HashSet<int> m_present = [];
    private readonly HashSet<int> m_interest = [];
    private readonly ExpandedNodeMapper? m_mapper;
    private readonly Dictionary<(int Step, FrdFieldKind Kind), FrdFieldFamily> m_families = [];
    private FrdFieldFamily? m_current;
    private bool m_resolved;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the accumulator.
    /// </summary>
    /// <param name="facts">The deck's facts; null when the deck is not available.</param>
    /// <param name="deckPath">The deck, for node coordinates on demand; null when not available.</param>
    /// <param name="probes">The probes requested.</param>
    /// <param name="sink">Receives the responses and notes.</param>
    /// <param name="sectionStress">The shell and beam stress of an OUTPUT=2D deck; null when the deck has none or writes the expanded mesh.</param>
    public FrdResponseAccumulator(DeckFacts? facts, string? deckPath, IReadOnlyList<CcxProbeData> probes, CcxResponseSink sink, CcxSectionStress? sectionStress = null)
    {
        m_facts = facts;
        m_deckPath = deckPath;
        m_sink = sink;
        m_sectionStress = sectionStress;
        m_refuseVonMises = sectionStress == null && facts is { OutputIs2D: true, HasShellOrBeamElements: true };

        foreach (var probe in probes)
        {
            var kind = KindOf(probe.Quantity);
            if (kind == null || facts == null || !facts.NodeSets.TryGetValue(probe.SetName, out var nodes) || nodes.Count == 0)
                continue;

            m_targets.Add(new FrdProbeTarget(probe, kind.Value, nodes));
            m_probeNodes.UnionWith(nodes);
        }

        if (m_targets.Count > 0 && facts is { HasExpandedElements: true } && deckPath != null)
            m_mapper = new ExpandedNodeMapper();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Writes the responses of every step and field to the sink.
    /// </summary>
    public void Finish()
    {
        foreach (var ((step, kind), family) in m_families.OrderBy(pair => pair.Key.Step).ThenBy(pair => pair.Key.Kind))
        {
            // A block cut short before its first complete row (a file
            // truncated mid-write) yields nothing - and kills nothing else.
            if (family.Last.Count == 0 && family.Broken == null)
                continue;

            var columns = Columns(kind, family);
            if (family.Broken is { } broken)
            {
                foreach (var column in columns.Concat(ProbeColumns(kind)))
                    m_sink.Note(CcxNoteKind.Warning, step, column,
                        $"not reported: {Dataset(kind)} at node {broken.Node} reads '{broken.Text}' in the .frd, which is not a number");

                continue;
            }

            if (kind == FrdFieldKind.Stress && (m_refuseVonMises || family.Problem != null))
            {
                var reason = family.Problem == null ? string.Empty : $", and the section-force recovery cannot run: {family.Problem}";
                foreach (var column in columns.Concat(ProbeColumns(kind)))
                    m_sink.Note(CcxNoteKind.Warning, step, column,
                        $"not reported: with OUTPUT=2D ccx maps shell and beam stresses onto the mid-surface or the beam axis, where the bending stress is gone (an S8R cantilever read 0.81 against 75 analytic){reason}; remove OUTPUT=2D from the file cards to get the expanded-mesh stress");

                continue;
            }

            AddValues(step, kind, family);
            foreach (var target in m_targets.Where(target => target.Kind == kind))
                AddProbe(step, family, target);

            if (kind == FrdFieldKind.Stress && m_sectionStress?.Method() is { } method)
            {
                foreach (var column in columns.Concat(ProbeColumns(kind)))
                    m_sink.Note(CcxNoteKind.Info, step, column, method);
            }

            if (family.LastIsModeShape)
            {
                foreach (var column in columns.Concat(ProbeColumns(kind)))
                    m_sink.Note(CcxNoteKind.Info, step, column,
                        $"a mode shape (mode {family.LastMode}, normalised), not a physical response");
            }
        }
    }

    private void AddValues(int step, FrdFieldKind kind, FrdFieldFamily family)
    {
        var last = family.Last;
        switch (kind)
        {
            case FrdFieldKind.Temperature:
                m_sink.Add(step, "max_temp", last.Max);
                m_sink.Add(step, "min_temp", last.Min);
                // A transient run carries one block per increment - the peak
                // over the step's history is a response of its own.
                if (family.Blocks > 1)
                    m_sink.Add(step, "max_temp_any_step", family.AnyBlockMax);
                break;

            case FrdFieldKind.Displacement:
                m_sink.Add(step, "max_disp_magnitude", last.Max);
                m_sink.Add(step, "max_disp_x", last.MaxAbs[0]);
                m_sink.Add(step, "max_disp_y", last.MaxAbs[1]);
                m_sink.Add(step, "max_disp_z", last.MaxAbs[2]);
                break;

            case FrdFieldKind.Stress:
                m_sink.Add(step, "max_von_mises", last.Max);
                m_sink.Add(step, "min_von_mises", last.Min);
                break;
        }
    }

    private void AddProbe(int step, FrdFieldFamily family, FrdProbeTarget target)
    {
        var perNode = new List<double>();
        var extremes = new List<double>();

        foreach (var (_, resultNodes) in target.Mapping)
        {
            var samples = resultNodes.Where(family.Samples.ContainsKey).Select(node => family.Samples[node]).ToList();
            if (samples.Count == 0)
                continue;

            perNode.Add(samples.Average());
            extremes.AddRange(samples);
        }

        var column = ProbeColumn(target.Probe);
        if (perNode.Count == 0)
        {
            if (target.Unresolved > 0)
                m_sink.Note(CcxNoteKind.Warning, step, column,
                    $"not reported: no result node of the .frd stands for any node of set {target.Probe.SetName}");

            return;
        }

        // Max and min run over every result node standing for the set (a beam
        // node's section nodes, a shell node's top and bottom); the average
        // weights each deck node once.
        m_sink.Add(step, column, target.Probe.Aggregate switch
        {
            CcxProbeAggregate.Min => extremes.Min(),
            CcxProbeAggregate.Avg => perNode.Average(),
            _ => extremes.Max()
        });

        if (target.ByNearest > 0)
            m_sink.Note(CcxNoteKind.Info, step, column,
                $"{target.ByNearest} of {target.DeckNodes.Count} nodes of set {target.Probe.SetName} have no result node at their own position (mid-side or beam nodes of the expanded mesh); the nearest result nodes were taken");

        if (target.Unresolved > 0)
            m_sink.Note(CcxNoteKind.Info, step, column,
                $"{target.Unresolved} of {target.DeckNodes.Count} nodes of set {target.Probe.SetName} have no result node in the .frd and were left out");
    }

    // The probe sets' nodes either appear in the .frd under their own numbers
    // (solid elements, OUTPUT=2D) or are stood for by expanded nodes found by
    // position. Resolved once, when the geometry section is complete.
    private void ResolveProbes()
    {
        m_resolved = true;
        var missing = new HashSet<int>();

        foreach (var target in m_targets)
        {
            foreach (var node in target.DeckNodes)
            {
                if (m_present.Contains(node))
                    target.Mapping[node] = [node];
                else
                    missing.Add(node);
            }
        }

        var coordinates = missing.Count > 0 && m_mapper is { Count: > 0 } && m_deckPath != null
            ? DeckFactsReader.ReadCoordinates(m_deckPath, missing)
            : new Dictionary<int, double[]>();

        foreach (var target in m_targets)
        {
            foreach (var node in target.DeckNodes.Where(node => !target.Mapping.ContainsKey(node)))
            {
                var atPosition = false;
                var mapped = coordinates.TryGetValue(node, out var point) ? m_mapper!.Map(point, out atPosition) : [];
                if (mapped.Length == 0)
                {
                    target.Unresolved++;
                    continue;
                }

                target.Mapping[node] = mapped;
                if (atPosition)
                    target.ByPosition++;
                else
                    target.ByNearest++;
            }

            foreach (var resultNodes in target.Mapping.Values)
                m_interest.UnionWith(resultNodes);
        }
    }

    private static FrdFieldKind? KindOf(string quantity)
    {
        if (quantity.Equals(CcxResponseExtractor.QUANTITY_TEMPERATURE, StringComparison.OrdinalIgnoreCase)
            || quantity.Equals(DATASET_TEMPERATURE, StringComparison.OrdinalIgnoreCase))
            return FrdFieldKind.Temperature;

        if (quantity.Equals(CcxResponseExtractor.QUANTITY_VON_MISES, StringComparison.OrdinalIgnoreCase))
            return FrdFieldKind.Stress;

        if (quantity.Equals(CcxResponseExtractor.QUANTITY_DISPLACEMENT_MAGNITUDE, StringComparison.OrdinalIgnoreCase))
            return FrdFieldKind.Displacement;

        return null;
    }

    private static FrdFieldKind? KindOf(FrdBlockHeader header)
    {
        FrdFieldKind? kind = header.Name switch
        {
            DATASET_TEMPERATURE => FrdFieldKind.Temperature,
            DATASET_DISPLACEMENT => FrdFieldKind.Displacement,
            DATASET_STRESS => FrdFieldKind.Stress,
            _ => null
        };

        return kind != null && header.Components.Count >= FrdFieldStatistics.ComponentsOf(kind.Value) ? kind : null;
    }

    private static string Dataset(FrdFieldKind kind)
    {
        return kind switch
        {
            FrdFieldKind.Temperature => DATASET_TEMPERATURE,
            FrdFieldKind.Displacement => DATASET_DISPLACEMENT,
            _ => DATASET_STRESS
        };
    }

    private static IEnumerable<string> Columns(FrdFieldKind kind, FrdFieldFamily family)
    {
        return kind switch
        {
            FrdFieldKind.Temperature => family.Blocks > 1 ? ["max_temp", "min_temp", "max_temp_any_step"] : ["max_temp", "min_temp"],
            FrdFieldKind.Displacement => ["max_disp_magnitude", "max_disp_x", "max_disp_y", "max_disp_z"],
            _ => ["max_von_mises", "min_von_mises"]
        };
    }

    private IEnumerable<string> ProbeColumns(FrdFieldKind kind)
    {
        return m_targets.Where(target => target.Kind == kind).Select(target => ProbeColumn(target.Probe));
    }

    private static string ProbeColumn(CcxProbeData probe)
    {
        return $"{probe.Aggregate.ToString().ToLowerInvariant()}_{probe.Quantity.ToLowerInvariant()}@{probe.SetName.ToLowerInvariant()}";
    }

    #endregion

    #region IFrdResultVisitor

    public void Node(int id, double x, double y, double z)
    {
        if (m_probeNodes.Contains(id))
            m_present.Add(id);

        if (m_mapper != null && id > m_facts!.MaxNodeId)
            m_mapper.Add(id, x, y, z);
    }

    public void BeginBlock(FrdBlockHeader header)
    {
        if (!m_resolved)
            ResolveProbes();

        var kind = KindOf(header);
        if (kind == null)
        {
            m_current = null;
            return;
        }

        if (!m_families.TryGetValue((header.Step, kind.Value), out m_current))
            m_families[(header.Step, kind.Value)] = m_current = new FrdFieldFamily(kind.Value);

        // A later block of the same field in the same step replaces the
        // earlier one: the response is the step's final state.
        m_current.Begin(header);
        if (kind == FrdFieldKind.Stress && m_sectionStress != null)
            m_current.Problem = m_sectionStress.BeginBlock(header);
    }

    public void Row(int node, double[] values)
    {
        if (m_current == null)
            return;

        var scalar = m_current.Kind == FrdFieldKind.Stress && m_sectionStress != null
            ? m_sectionStress.VonMises(node, values)
            : FrdFieldStatistics.Scalar(m_current.Kind, values);
        m_current.Last.Add(values, scalar);
        m_current.AnyBlockMax = m_current.Blocks == 1 && m_current.Last.Count == 1 ? scalar : System.Math.Max(m_current.AnyBlockMax, scalar);

        if (m_interest.Contains(node))
            m_current.Samples[node] = scalar;
    }

    public void BrokenRow(int node, string text)
    {
        if (m_current != null)
            m_current.Broken ??= (node, text);
    }

    public void EndBlock()
    {
        m_current = null;
    }

    public bool WantsGeometry => m_targets.Count > 0;

    #endregion
}
