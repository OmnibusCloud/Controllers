
namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Turns a read .dat file into responses, step by step: eigenfrequencies
/// numbered within their step (and by nodal diameter in a cyclic-symmetry
/// table), buckling factors, and reaction-force sums in the global frame -
/// the global total ccx printed where the deck asked for it, otherwise the
/// rows summed, with every row ccx printed in a local <c>*TRANSFORM</c>
/// frame rotated back through the deck's own transform. A set whose local
/// rows cannot be rotated gets no sum and a note, never a sum of local
/// components (a cylindrical-frame cantilever summed to 1262/-2524/0 where
/// the global reaction was 0/-50/100).
/// </summary>
internal static class DatResponseExtractor
{
    #region Functions

    /// <summary>
    /// Adds the responses of the file to the sink.
    /// </summary>
    /// <param name="dat">The read file.</param>
    /// <param name="facts">The deck's facts; null when the deck is not available.</param>
    /// <param name="deckPath">The deck, for node coordinates on demand; null when not available.</param>
    /// <param name="sink">Receives the responses and notes.</param>
    public static void Add(DatResultFile dat, DeckFacts? facts, string? deckPath, CcxResponseSink sink)
    {
        foreach (var step in dat.Steps)
        {
            foreach (var eigenfrequency in step.Eigenfrequencies)
            {
                var name = eigenfrequency.NodalDiameter == null
                    ? $"eigenfrequency_{eigenfrequency.Mode}"
                    : $"eigenfrequency_nd{eigenfrequency.NodalDiameter}_{eigenfrequency.Mode}";

                sink.Add(step.Number, name, eigenfrequency.Cycles);
            }

            if (step.UnknownEigenvalueTables > 0)
                sink.Note(CcxNoteKind.Warning, step.Number, null,
                    $"{step.UnknownEigenvalueTables} eigenvalue table(s) in a layout the controller does not read: their frequencies are not reported");

            for (var i = 0; i < step.BucklingFactors.Count; i++)
                sink.Add(step.Number, $"buckling_factor_{i + 1}", step.BucklingFactors[i]);

            foreach (var block in step.ForceBlocks)
                AddForces(step.Number, block, facts, deckPath, sink);

            if (step.ModeShapeForceBlocks > 0)
                sink.Note(CcxNoteKind.Info, step.Number, null,
                    "the reaction forces ccx printed per mode after the eigenvalue solve are mode shapes and are not summed");
        }
    }

    private static void AddForces(int step, DatForceBlock block, DeckFacts? facts, string? deckPath, CcxResponseSink sink)
    {
        var set = block.SetName.ToLowerInvariant();
        string[] columns = [$"rf_sum_x@{set}", $"rf_sum_y@{set}", $"rf_sum_z@{set}"];

        // ccx sums the global components itself for TOTALS=YES/ONLY, before
        // any transform touches the rows: exact, and the only sum a
        // TOTALS=ONLY print offers.
        var sum = block.Total;
        var rotated = 0;
        DeckTransform? frame = null;

        if (sum == null)
        {
            sum = new double[3];
            var coordinates = Coordinates(block, facts, deckPath);

            foreach (var row in block.Rows)
            {
                double[] global = [row.Fx, row.Fy, row.Fz];
                if (row.Local)
                {
                    var transform = facts?.TransformOf(row.Node);
                    var reason = transform == null
                        ? facts == null ? "the deck is not available to resolve the frame" : $"no *TRANSFORM of the deck covers node {row.Node}"
                        : transform.Cylindrical && !coordinates.ContainsKey(row.Node) ? $"node {row.Node} has no coordinates in the deck" : null;

                    if (reason != null)
                    {
                        foreach (var column in columns)
                            sink.Note(CcxNoteKind.Warning, step, column,
                                $"not reported: ccx printed the reaction forces of set {block.SetName} in a local *TRANSFORM frame and {reason}; print them with *NODE PRINT, GLOBAL=YES or TOTALS=YES");

                        return;
                    }

                    global = transform!.ToGlobal(global, transform.Cylindrical ? coordinates[row.Node] : [0.0, 0.0, 0.0]);
                    frame = transform;
                    rotated++;
                }

                for (var axis = 0; axis < 3; axis++)
                    sum[axis] += global[axis];
            }
        }

        for (var axis = 0; axis < 3; axis++)
            sink.Add(step, columns[axis], sum[axis]);

        if (rotated > 0)
        {
            foreach (var column in columns)
                sink.Note(CcxNoteKind.Info, step, column,
                    $"{rotated} of {block.Rows.Count} rows were printed in the local frame of *TRANSFORM, NSET={frame!.SetName} and rotated to the global frame");
        }
    }

    // A cylindrical frame is built at the node: its coordinates come from
    // the deck, read once per block for the rows that need them.
    private static Dictionary<int, double[]> Coordinates(DatForceBlock block, DeckFacts? facts, string? deckPath)
    {
        if (facts == null || deckPath == null)
            return [];

        var nodes = new HashSet<int>();
        foreach (var row in block.Rows)
        {
            if (row.Local && facts.TransformOf(row.Node) is { Cylindrical: true })
                nodes.Add(row.Node);
        }

        return nodes.Count == 0 ? [] : DeckFactsReader.ReadCoordinates(deckPath, nodes);
    }

    #endregion
}
