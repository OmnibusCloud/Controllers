namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Maps a deck node of an expanded element to the result nodes that stand
/// for it in a 3D-output .frd. ccx expands shells, beams and plane elements
/// onto a 3D mesh whose nodes are new numbers (above the deck's highest),
/// and with the default <c>OUTPUT=3D</c> the file holds those nodes only -
/// an S8R deck of nodes 1-289 came back as nodes 370-1236 and every probe
/// over a deck set found nothing. The expansion is geometric, so geometry
/// maps it back: a deck node maps to the result nodes at its own position
/// (the mid-surface node of a shell corner node) when there are any, and
/// otherwise to the result nodes nearest to it, all of them when they tie
/// (the top and bottom nodes of a quadratic shell's mid-side node, the
/// section nodes of a beam node). Positions compare within a tolerance set
/// by the six significant digits the .frd stores.
/// </summary>
public sealed class ExpandedNodeMapper
{
    #region Constants

    // The .frd prints %12.5E: six significant digits, a relative rounding of
    // at most 5e-6 of the coordinate's magnitude. Twice that, on the largest
    // magnitude in the file, is "the same position".
    private const double RELATIVE_TOLERANCE = 1e-5;

    private const int GRID_CELLS_PER_AXIS = 100;

    #endregion

    #region Fields

    private readonly List<int> m_ids = [];
    private readonly List<double> m_points = [];
    private readonly Dictionary<(int, int, int), List<int>> m_cells = [];
    private readonly double[] m_minimum = [double.MaxValue, double.MaxValue, double.MaxValue];
    private readonly int[] m_cellCount = new int[3];
    private double m_maximumMagnitude = 1.0;
    private double m_cellSize = 1.0;
    private bool m_built;

    #endregion

    #region Functions

    /// <summary>
    /// Adds a result node of the expanded mesh.
    /// </summary>
    /// <param name="id">Result node id.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="z">Z coordinate.</param>
    public void Add(int id, double x, double y, double z)
    {
        m_ids.Add(id);
        m_points.Add(x);
        m_points.Add(y);
        m_points.Add(z);
        m_built = false;
    }

    /// <summary>
    /// The result nodes standing for a deck node at the given position.
    /// </summary>
    /// <param name="point">The deck node's coordinates.</param>
    /// <param name="atPosition">True when the result nodes sit at the deck node's own
    /// position; false when the nearest ones were taken.</param>
    /// <returns>The result node ids; empty when the expanded mesh holds no node at all.</returns>
    public int[] Map(double[] point, out bool atPosition)
    {
        atPosition = false;
        if (m_ids.Count == 0)
            return [];

        Build();
        var tolerance = Tolerance;
        var best = double.MaxValue;
        var found = new List<int>();
        var center = Cell(point);

        if (!InsideGrid(center))
        {
            // A point outside the expanded mesh's box: no ring bound holds,
            // every node is looked at (rare - such a node is a solid node the
            // file lists under its own number, and never reaches the mapper).
            for (var index = 0; index < m_ids.Count; index++)
                Consider(index, point, tolerance, found, ref best);
        }
        else
        {
            // Rings of cells outward from the point's own; a ring whose inner
            // face is farther than the best distance so far cannot improve it.
            for (var ring = 0; ring <= GRID_CELLS_PER_AXIS + 1; ring++)
            {
                if (ring > 0 && (ring - 1) * m_cellSize > best + tolerance)
                    break;

                Search(center, ring, point, tolerance, found, ref best);
            }
        }

        atPosition = found.Count > 0 && best <= tolerance;
        return found.ToArray();
    }

    private bool InsideGrid((int X, int Y, int Z) cell)
    {
        return cell.X >= 0 && cell.X <= m_cellCount[0]
               && cell.Y >= 0 && cell.Y <= m_cellCount[1]
               && cell.Z >= 0 && cell.Z <= m_cellCount[2];
    }

    private void Search((int X, int Y, int Z) center, int ring, double[] point, double tolerance, List<int> found, ref double best)
    {
        for (var dx = -ring; dx <= ring; dx++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dz = -ring; dz <= ring; dz++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Max(System.Math.Abs(dy), System.Math.Abs(dz))) != ring)
                        continue;

                    if (!m_cells.TryGetValue((center.X + dx, center.Y + dy, center.Z + dz), out var members))
                        continue;

                    foreach (var index in members)
                        Consider(index, point, tolerance, found, ref best);
                }
            }
        }
    }

    private void Consider(int index, double[] point, double tolerance, List<int> found, ref double best)
    {
        var distance = Distance(index, point);
        if (distance < best - tolerance)
        {
            best = distance;
            found.Clear();
            found.Add(m_ids[index]);
        }
        else if (distance <= best + tolerance)
        {
            found.Add(m_ids[index]);
            best = System.Math.Min(best, distance);
        }
    }

    private double Distance(int index, double[] point)
    {
        var dx = m_points[3 * index] - point[0];
        var dy = m_points[3 * index + 1] - point[1];
        var dz = m_points[3 * index + 2] - point[2];
        return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void Build()
    {
        if (m_built)
            return;

        var maximum = new[] { double.MinValue, double.MinValue, double.MinValue };
        for (var i = 0; i < m_ids.Count; i++)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var value = m_points[3 * i + axis];
                m_minimum[axis] = System.Math.Min(m_minimum[axis], value);
                maximum[axis] = System.Math.Max(maximum[axis], value);
                m_maximumMagnitude = System.Math.Max(m_maximumMagnitude, System.Math.Abs(value));
            }
        }

        var diagonal = 0.0;
        for (var axis = 0; axis < 3; axis++)
            diagonal += (maximum[axis] - m_minimum[axis]) * (maximum[axis] - m_minimum[axis]);

        m_cellSize = System.Math.Max(System.Math.Sqrt(diagonal) / GRID_CELLS_PER_AXIS, 10 * Tolerance);
        m_cells.Clear();

        for (var i = 0; i < m_ids.Count; i++)
        {
            var cell = Cell([m_points[3 * i], m_points[3 * i + 1], m_points[3 * i + 2]]);
            if (!m_cells.TryGetValue(cell, out var members))
                m_cells[cell] = members = [];

            members.Add(i);
        }

        for (var axis = 0; axis < 3; axis++)
            m_cellCount[axis] = (int)System.Math.Floor((maximum[axis] - m_minimum[axis]) / m_cellSize);

        m_built = true;
    }

    private (int X, int Y, int Z) Cell(double[] point)
    {
        return (
            (int)System.Math.Floor((point[0] - m_minimum[0]) / m_cellSize),
            (int)System.Math.Floor((point[1] - m_minimum[1]) / m_cellSize),
            (int)System.Math.Floor((point[2] - m_minimum[2]) / m_cellSize));
    }

    #endregion

    #region Properties

    /// <summary>Number of result nodes added.</summary>
    public int Count => m_ids.Count;

    /// <summary>The distance within which two positions are the same.</summary>
    public double Tolerance => RELATIVE_TOLERANCE * m_maximumMagnitude;

    #endregion
}
