namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The bending stress of a deck's shells from the section prints of their
/// edges. A print of one element edge gives the force F and the moment M
/// (about the cut's centre) that act on the element through the cut of area
/// A = length × t. With the stress linear through the thickness, the
/// bending moment adds 6 (M × ν) / (t A) to the traction on the cut at the
/// top face and takes it off at the bottom one, ν the shell normal - the
/// ± 6M/t² of shell theory. At every element corner the two edges that meet
/// there give two such tractions, from which the in-plane bending stress
/// tensor follows by least squares; a node takes the mean over the elements
/// that share it, a mid-side node the mean of its edge's corners
/// (<see cref="ShellBending"/>). The membrane part is the .frd's own: under
/// OUTPUT=2D its stress at a shell node is the mean through the thickness.
/// Everything comes from the printed vectors - the cut normals, and the
/// shell normal as their cross product at the corner - so the deformed
/// geometry of an NLGEOM step needs no displacements.
/// <para>
/// Measured against ccx 2.22 (2026-10-29): a cantilever plate 100 × 20 × 2
/// of S8R under a tip load, 75 analytic at the root: 74.999 (OUTPUT=2D gave
/// 0.81, the expanded mesh 75.0). The same plate of S4: 73.125, the value
/// of the expanded mesh (73.13); an S4 element bends at constant curvature,
/// so its root stress is the beam's at half an element in. A CQUAD4 strip
/// 10 × 1 × 0.1 converted from Nastran, 6000 analytic: 5850, 5925, 5962.5
/// on 20 × 2, 40 × 4, 80 × 8 elements, as the expanded mesh, approaching
/// 6000 as the element shrinks (5981.25 on 160 × 16).
/// </para>
/// </summary>
public static class ShellStressRecovery
{
    #region Constants

    private const double TIME_TOLERANCE = 1e-5;

    private const double PARALLEL_LIMIT = 1e-3;

    #endregion

    #region Functions

    /// <summary>
    /// Recovers the shell nodes' bending stress of one increment.
    /// </summary>
    /// <param name="structure">The deck's shells, their thickness and the controller's edge surfaces.</param>
    /// <param name="sections">The section-print blocks of the step, by surface name.</param>
    /// <param name="time">The total time of the .frd block the values stand for.</param>
    /// <param name="problem">Why nothing was recovered; null on success.</param>
    /// <returns>Shell node → its bending part; null when an edge has no print at that time.</returns>
    public static Dictionary<int, ShellBending>? Recover(DeckStructure structure, IReadOnlyDictionary<string, DatSectionBlock> sections, double time, out string? problem)
    {
        var edges = EdgePrints(structure, sections, time, out problem);
        if (edges == null)
            return null;

        var sums = new Dictionary<int, (Vector3D Normal, double[] Bending, int Count)>();
        foreach (var element in structure.Shells.Values)
        {
            var corners = DeckElement.CornersOf(element.Type);
            var thickness = structure.ShellThickness[element.Id];
            var cuts = new (Vector3D Force, Vector3D Normal, Vector3D Moment, double Area)[corners];
            for (var edge = 0; edge < corners; edge++)
            {
                var (a, b) = element.EdgeCorners(edge);
                var (block, owner) = edges[(System.Math.Min(a, b), System.Math.Max(a, b))];

                // The print is the action on its owner element; the
                // neighbour across the cut feels the opposite one.
                var sign = owner == element.Id ? 1.0 : -1.0;
                cuts[edge] = (block.Force * sign, block.Normal.Unit() * sign, block.Moment * sign, block.Area);
            }

            var values = Corners(cuts, thickness);
            for (var corner = 0; corner < corners; corner++)
                Add(sums, element.Nodes[corner], values[corner].Normal, values[corner].Bending);

            // Mid-side nodes follow the corners: node corners + k sits on edge k.
            for (var edge = 0; corners + edge < element.Nodes.Length && edge < corners; edge++)
            {
                var (first, second) = (values[edge], values[(edge + 1) % corners]);
                Add(sums, element.Nodes[corners + edge], first.Normal + second.Normal,
                    first.Bending.Zip(second.Bending, (x, y) => 0.5 * (x + y)).ToArray());
            }
        }

        return sums.ToDictionary(pair => pair.Key,
            pair => new ShellBending(pair.Value.Normal.Unit(), pair.Value.Bending.Select(value => value / pair.Value.Count).ToArray()));
    }

    private static Dictionary<(int, int), (DatSectionBlock Block, int Owner)>? EdgePrints(
        DeckStructure structure, IReadOnlyDictionary<string, DatSectionBlock> sections, double time, out string? problem)
    {
        problem = null;
        var edges = new Dictionary<(int, int), (DatSectionBlock, int)>();
        foreach (var (name, (elementId, face)) in structure.EdgeSurfaces)
        {
            if (!structure.Shells.TryGetValue(elementId, out var element) || face < 3 || face - 3 >= DeckElement.CornersOf(element.Type))
                continue;

            if (!sections.TryGetValue(name, out var block))
            {
                problem = $"the .dat has no section print of surface {name.ToUpperInvariant()}";
                return null;
            }

            if (System.Math.Abs(block.Time - time) > TIME_TOLERANCE * System.Math.Max(1.0, System.Math.Abs(time)))
            {
                problem = $"the last section prints of the step are at time {block.Time:G7}, the stress block at {time:G7}";
                return null;
            }

            var (a, b) = element.EdgeCorners(face - 3);
            edges[(System.Math.Min(a, b), System.Math.Max(a, b))] = (block, elementId);
        }

        foreach (var element in structure.Shells.Values)
        {
            for (var edge = 0; edge < DeckElement.CornersOf(element.Type); edge++)
            {
                var (a, b) = element.EdgeCorners(edge);
                if (!edges.ContainsKey((System.Math.Min(a, b), System.Math.Max(a, b))))
                {
                    problem = $"edge {a}-{b} of element {element.Id} has no section print";
                    return null;
                }
            }
        }

        return edges;
    }

    private static (Vector3D Normal, double[] Bending)[] Corners((Vector3D Force, Vector3D Normal, Vector3D Moment, double Area)[] cuts, double thickness)
    {
        var count = cuts.Length;

        // The shell normal at a corner is the cross product of the outward
        // normals of the edge before and the edge after it; a corner whose
        // edges run straight on takes the element's mean normal.
        var normals = new Vector3D[count];
        var mean = new Vector3D(0, 0, 0);
        for (var corner = 0; corner < count; corner++)
        {
            normals[corner] = cuts[(corner + count - 1) % count].Normal.Cross(cuts[corner].Normal);
            mean += normals[corner];
        }

        mean = mean.Unit();
        var values = new (Vector3D, double[])[count];
        for (var corner = 0; corner < count; corner++)
        {
            var normal = normals[corner].Length > PARALLEL_LIMIT ? normals[corner].Unit() : mean;
            if (normal.Dot(mean) < 0)
                normal = -normal;

            values[corner] = (normal, Corner(cuts[(corner + count - 1) % count], cuts[corner], normal, thickness));
        }

        return values;
    }

    private static double[] Corner((Vector3D Force, Vector3D Normal, Vector3D Moment, double Area) before,
        (Vector3D Force, Vector3D Normal, Vector3D Moment, double Area) after, Vector3D normal, double thickness)
    {
        var first = (before.Normal - normal * before.Normal.Dot(normal)).Unit();
        var second = normal.Cross(first);

        var bending = new LeastSquares();
        foreach (var cut in new[] { before, after })
        {
            var direction = new Vector3D(cut.Normal.Dot(first), cut.Normal.Dot(second), 0).Unit();
            var surface = cut.Moment.Cross(normal) * (6.0 / (thickness * cut.Area));
            bending.Add(direction.X, direction.Y, surface.Dot(first), surface.Dot(second));
        }

        // The local tensor (sxx, syy, sxy) in the global frame.
        var local = bending.Tensor();
        return Global(local[0], local[1], local[2], first, second);
    }

    private static double[] Global(double sxx, double syy, double sxy, Vector3D first, Vector3D second)
    {
        double Component(double a1, double b1, double a2, double b2)
        {
            return sxx * a1 * b1 + syy * a2 * b2 + sxy * (a1 * b2 + a2 * b1);
        }

        return
        [
            Component(first.X, first.X, second.X, second.X),
            Component(first.Y, first.Y, second.Y, second.Y),
            Component(first.Z, first.Z, second.Z, second.Z),
            Component(first.X, first.Y, second.X, second.Y),
            Component(first.Y, first.Z, second.Y, second.Z),
            Component(first.Z, first.X, second.Z, second.X)
        ];
    }

    private static void Add(Dictionary<int, (Vector3D Normal, double[] Bending, int Count)> sums, int node, Vector3D normal, double[] bending)
    {
        if (!sums.TryGetValue(node, out var entry))
        {
            sums[node] = (normal.Unit(), (double[])bending.Clone(), 1);
            return;
        }

        // Elements of opposite orientation share a node with opposite
        // normals: the bending of the face their normal points to has the
        // opposite sign too, so both flip together.
        var sign = normal.Dot(entry.Normal) < 0 ? -1.0 : 1.0;
        for (var i = 0; i < 6; i++)
            entry.Bending[i] += sign * bending[i];

        sums[node] = (entry.Normal + normal.Unit() * sign, entry.Bending, entry.Count + 1);
    }

    #endregion
}
