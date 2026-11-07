namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One <c>*TRANSFORM</c> card of a deck: the node set it applies to and the
/// local frame it declares, rectangular (<c>TYPE=R</c>, the default: two
/// vectors a and b, e1 along a, e2 the part of b normal to a) or cylindrical
/// (<c>TYPE=C</c>: two points on the axis, e3 along the axis, e1 radial at
/// the node). The frame is the one ccx builds in <c>transformatrix.f</c>,
/// node coordinates included, so a vector ccx printed in that frame comes
/// back to the global frame exactly.
/// </summary>
public sealed class DeckTransform
{
    #region Constants

    private const double ON_AXIS = 1e-10;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the transform.
    /// </summary>
    /// <param name="setName">Node set the card names.</param>
    /// <param name="cylindrical">True for <c>TYPE=C</c>.</param>
    /// <param name="points">The six numbers of the data line: point a, point b.</param>
    public DeckTransform(string setName, bool cylindrical, double[] points)
    {
        SetName = setName;
        Cylindrical = cylindrical;
        Points = points;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Rotates a vector given in the local frame at a node back to the global frame.
    /// </summary>
    /// <param name="local">The three local components ccx printed.</param>
    /// <param name="node">The node's coordinates (used by a cylindrical frame only).</param>
    /// <returns>The global components.</returns>
    public double[] ToGlobal(ReadOnlySpan<double> local, ReadOnlySpan<double> node)
    {
        var (e1, e2, e3) = Frame(node);
        var global = new double[3];

        for (var j = 0; j < 3; j++)
            global[j] = local[0] * e1[j] + local[1] * e2[j] + local[2] * e3[j];

        return global;
    }

    // transformatrix.f, line for line: a(j,1)=e1(j), a(j,2)=e2(j), a(j,3)=e3(j),
    // and ccx prints local_k = sum_j fn_j * a(j,k).
    private (double[] E1, double[] E2, double[] E3) Frame(ReadOnlySpan<double> node)
    {
        var e1 = new double[3];
        var e2 = new double[3];
        var e3 = new double[3];

        if (!Cylindrical)
        {
            for (var j = 0; j < 3; j++)
            {
                e1[j] = Points[j];
                e2[j] = Points[3 + j];
            }

            Normalize(e1);
            var along = Dot(e1, e2);
            for (var j = 0; j < 3; j++)
                e2[j] -= along * e1[j];

            Normalize(e2);
            Cross(e1, e2, e3);
            return (e1, e2, e3);
        }

        for (var j = 0; j < 3; j++)
        {
            e1[j] = node[j] - Points[j];
            e3[j] = Points[3 + j] - Points[j];
        }

        Normalize(e3);
        var axial = Dot(e1, e3);
        for (var j = 0; j < 3; j++)
            e1[j] -= axial * e3[j];

        // A node on the axis has no radial direction; ccx takes a fixed
        // perpendicular there, and so does the rotation back.
        if (Length(e1) < ON_AXIS)
        {
            if (System.Math.Abs(e3[0]) > ON_AXIS)
                (e1[0], e1[1], e1[2]) = (-e3[1] / e3[0], 1.0, 0.0);
            else if (System.Math.Abs(e3[1]) > ON_AXIS)
                (e1[0], e1[1], e1[2]) = (0.0, -e3[2] / e3[1], 1.0);
            else
                (e1[0], e1[1], e1[2]) = (1.0, 0.0, -e3[0] / e3[2]);
        }

        Normalize(e1);
        Cross(e3, e1, e2);
        return (e1, e2, e3);
    }

    private static double Dot(double[] a, double[] b)
    {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    private static double Length(double[] a)
    {
        return System.Math.Sqrt(Dot(a, a));
    }

    private static void Normalize(double[] a)
    {
        var length = Length(a);
        for (var j = 0; j < 3; j++)
            a[j] /= length;
    }

    private static void Cross(double[] a, double[] b, double[] result)
    {
        result[0] = a[1] * b[2] - b[1] * a[2];
        result[1] = a[2] * b[0] - a[0] * b[2];
        result[2] = a[0] * b[1] - b[0] * a[1];
    }

    #endregion

    #region Properties

    /// <summary>Node set the card applies to.</summary>
    public string SetName { get; }

    /// <summary>True for a cylindrical frame (<c>TYPE=C</c>).</summary>
    public bool Cylindrical { get; }

    /// <summary>The six numbers of the data line: point a (0-2), point b (3-5).</summary>
    public IReadOnlyList<double> Points { get; }

    #endregion
}
