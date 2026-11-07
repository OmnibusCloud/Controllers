using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The cross section of a beam element as the deck gives it: <c>*BEAM
/// SECTION</c> with <c>SECTION=RECT</c> (the thickness in the 1- and the
/// 2-direction) or <c>CIRC</c> (the diameters in the 1- and the 2-direction:
/// ccx expands a <c>5, 5</c> section to a circle of radius 2.5, measured on
/// the tip deflection), or <c>*BEAM GENERAL SECTION</c> with
/// <c>SECTION=PIPE</c> (outer radius, wall thickness) or <c>BOX</c> (the
/// outer size in the 1- and the 2-direction, then the walls t1 and t3 across
/// the 1-direction and t2 and t4 across the 2-direction, after
/// beamintscheme.f).
/// <para>
/// <see cref="VonMises"/> turns the section forces ccx writes for a beam
/// node under <c>*EL FILE, SECTION FORCES</c> (map3dto1d2d.f) into the
/// largest von Mises stress over the section: the normal stress
/// <c>N/A ± M1/W1 ± M2/W2</c> at the extreme fibres, with the shear the
/// fibre carries - the torsion and the transverse shear at the mid-sides,
/// the transverse shear alone at the centre.
/// </para>
/// </summary>
public sealed class DeckBeamSection
{
    #region Constants

    // Saint-Venant torsion of a rectangle (Timoshenko and Goodier, Theory of
    // Elasticity, table of section 109): the long side over the short side,
    // the factor of tau_max = T / (alpha * long * short^2), and the stress at
    // the middle of the short side over tau_max.
    private static readonly double[] TORSION_RATIO = [1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 10.0];

    private static readonly double[] TORSION_ALPHA = [0.208, 0.231, 0.246, 0.258, 0.267, 0.282, 0.291, 0.312];

    private static readonly double[] TORSION_SHORT_SIDE = [1.000, 0.859, 0.795, 0.766, 0.753, 0.745, 0.744, 0.742];

    private const double TORSION_ALPHA_LIMIT = 1.0 / 3.0;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the section.
    /// </summary>
    /// <param name="kind">The section kind: "RECT", "CIRC", "PIPE" or "BOX".</param>
    /// <param name="dimensions">The numbers of the first data line, as written.</param>
    public DeckBeamSection(string kind, double[] dimensions)
    {
        Kind = kind;
        Dimensions = dimensions;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Why the controller cannot turn this section's forces into a stress;
    /// null when it can.
    /// </summary>
    /// <returns>The reason, for a note; null for a supported section.</returns>
    public string? Unsupported()
    {
        switch (Kind)
        {
            case "RECT" when Dimensions.Length >= 2 && Dimensions[0] > 0 && Dimensions[1] > 0:
            case "PIPE" when Dimensions.Length >= 2 && Dimensions[0] > 0 && Dimensions[1] > 0 && Dimensions[1] <= Dimensions[0]:
                return null;

            case "BOX" when Dimensions.Length >= 6 && Dimensions.All(value => value > 0)
                            && Dimensions[0] > Dimensions[2] + Dimensions[4] && Dimensions[1] > Dimensions[3] + Dimensions[5]:
                return Dimensions[2].Is(Dimensions[4]) && Dimensions[3].Is(Dimensions[5])
                    ? null
                    : "a BOX section with unequal opposite walls, whose centroid is off the axis the section forces are taken about";

            case "CIRC":
                // A cantilever's root moment of 10 000 came back as 10 848,
                // the mid-span 5 000 as 5 392 (B32, ccx 2.22): the 2x2 Gauss
                // rule of map3dto1d2d.f over the mapped circle is that far off.
                return "a CIRC section, whose section forces ccx integrates 8 % high (a root moment of 10 000 came back as 10 848)";

            default:
                return $"a {Kind} section with dimensions {string.Join(", ", Dimensions)}";
        }
    }

    /// <summary>
    /// The largest von Mises stress over the section.
    /// </summary>
    /// <param name="forces">
    /// The section forces in the order of the .frd STRESS block under
    /// <c>SECTION FORCES</c>: shear in the 1-direction, shear in the
    /// 2-direction, axial force, torque, moment about the 1-direction,
    /// moment about the 2-direction.
    /// </param>
    /// <returns>The von Mises stress; NaN for a section <see cref="Unsupported"/> refuses.</returns>
    public double VonMises(double[] forces)
    {
        var shear1 = System.Math.Abs(forces[0]);
        var shear2 = System.Math.Abs(forces[1]);
        var axial = System.Math.Abs(forces[2]);
        var torque = System.Math.Abs(forces[3]);
        var moment1 = System.Math.Abs(forces[4]);
        var moment2 = System.Math.Abs(forces[5]);

        if (Unsupported() != null)
            return double.NaN;

        return Kind switch
        {
            "RECT" => Rectangle(shear1, shear2, axial, torque, moment1, moment2),
            "PIPE" => Pipe(shear1, shear2, axial, torque, moment1, moment2),
            _ => Box(shear1, shear2, axial, torque, moment1, moment2)
        };
    }

    public override string ToString()
    {
        return $"{Kind} {string.Join(", ", Dimensions)}";
    }

    private double Rectangle(double shear1, double shear2, double axial, double torque, double moment1, double moment2)
    {
        // a along the 1-direction, b along the 2-direction: the moment about
        // the 1-direction bends across b.
        var (a, b) = (Dimensions[0], Dimensions[1]);
        var area = a * b;
        var modulus1 = a * b * b / 6.0;
        var modulus2 = b * a * a / 6.0;
        var normal = axial / area;

        var (longSide, shortSide) = a >= b ? (a, b) : (b, a);
        var ratio = longSide / shortSide;
        var torsionMax = torque / (Interpolate(TORSION_ALPHA, ratio, TORSION_ALPHA_LIMIT) * longSide * shortSide * shortSide);
        var torsionShort = torsionMax * Interpolate(TORSION_SHORT_SIDE, ratio, TORSION_SHORT_SIDE[^1]);
        var torsionOnA = a >= b ? torsionMax : torsionShort;
        var torsionOnB = b >= a ? torsionMax : torsionShort;

        return Max(
            Point(normal + moment1 / modulus1 + moment2 / modulus2, 0.0),
            Point(normal + moment1 / modulus1, torsionOnA + 1.5 * shear1 / area),
            Point(normal + moment2 / modulus2, torsionOnB + 1.5 * shear2 / area),
            Point(normal, 1.5 * System.Math.Sqrt(shear1 * shear1 + shear2 * shear2) / area));
    }

    private double Pipe(double shear1, double shear2, double axial, double torque, double moment1, double moment2)
    {
        var outer = Dimensions[0];
        var inner = outer - Dimensions[1];
        var area = System.Math.PI * (outer * outer - inner * inner);
        var inertia = System.Math.PI * (System.Math.Pow(outer, 4) - System.Math.Pow(inner, 4)) / 4.0;
        var torsion = torque * outer / (2.0 * inertia);
        var shearFactor = 4.0 / 3.0 * (outer * outer + outer * inner + inner * inner) / (outer * outer + inner * inner);
        var normal = axial / area;

        return Max(
            Point(normal + System.Math.Sqrt(moment1 * moment1 + moment2 * moment2) * outer / inertia, torsion),
            Point(normal, torsion + shearFactor * System.Math.Sqrt(shear1 * shear1 + shear2 * shear2) / area));
    }

    private double Box(double shear1, double shear2, double axial, double torque, double moment1, double moment2)
    {
        // Walls t1 = t3 across the 1-direction (at x1 = ±a/2, spanning b) and
        // t2 = t4 across the 2-direction (at x2 = ±b/2, spanning a).
        var (a, b, across1, across2) = (Dimensions[0], Dimensions[1], Dimensions[2], Dimensions[3]);
        var (innerA, innerB) = (a - 2.0 * across1, b - 2.0 * across2);
        var area = a * b - innerA * innerB;
        var inertia1 = (a * b * b * b - innerA * innerB * innerB * innerB) / 12.0;
        var inertia2 = (b * a * a * a - innerB * innerA * innerA * innerA) / 12.0;
        var normal = axial / area;

        // Bredt's shear flow, and Jourawski's shear at the neutral axis cut
        // through the two walls that carry each shear force.
        var flow = torque / (2.0 * (a - across1) * (b - across2));
        var firstMoment1 = a * across2 * (b - across2) / 2.0 + across1 * (b / 2.0 - across2) * (b / 2.0 - across2);
        var firstMoment2 = b * across1 * (a - across1) / 2.0 + across2 * (a / 2.0 - across1) * (a / 2.0 - across1);
        var shearOnAcross1 = shear2 * firstMoment1 / (inertia1 * 2.0 * across1);
        var shearOnAcross2 = shear1 * firstMoment2 / (inertia2 * 2.0 * across2);

        return Max(
            Point(normal + moment1 * b / (2.0 * inertia1) + moment2 * a / (2.0 * inertia2), flow / System.Math.Min(across1, across2)),
            Point(normal + moment1 * b / (2.0 * inertia1), flow / across2 + shearOnAcross2),
            Point(normal + moment2 * a / (2.0 * inertia2), flow / across1 + shearOnAcross1));
    }

    private static double Point(double normal, double shear)
    {
        return System.Math.Sqrt(normal * normal + 3.0 * shear * shear);
    }

    private static double Max(params double[] values)
    {
        return values.Max();
    }

    private static double Interpolate(double[] values, double ratio, double limit)
    {
        if (ratio <= TORSION_RATIO[0])
            return values[0];

        for (var i = 1; i < TORSION_RATIO.Length; i++)
        {
            if (ratio <= TORSION_RATIO[i])
            {
                var share = (ratio - TORSION_RATIO[i - 1]) / (TORSION_RATIO[i] - TORSION_RATIO[i - 1]);
                return values[i - 1] + share * (values[i] - values[i - 1]);
            }
        }

        // Beyond the table the factor tends to its thin-strip limit as 1/ratio.
        var last = TORSION_RATIO[^1];
        return values[^1] + (limit - values[^1]) * (1.0 - last / ratio);
    }

    #endregion

    #region Properties

    /// <summary>The section kind: "RECT", "CIRC", "PIPE" or "BOX".</summary>
    public string Kind { get; }

    /// <summary>The numbers of the first data line, as written.</summary>
    public double[] Dimensions { get; }

    #endregion
}
