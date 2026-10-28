namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The kind of field a <see cref="FrdFieldStatistics"/> reduces.
/// </summary>
public enum FrdFieldKind
{
    /// <summary>NDTEMP: one component, the temperature.</summary>
    Temperature = 0,

    /// <summary>DISP: three components; the magnitude and the per-axis extremes.</summary>
    Displacement = 1,

    /// <summary>STRESS: six components; the von Mises stress.</summary>
    Stress = 2
}

/// <summary>
/// Running extremes of one nodal field, row by row, so a block of any size
/// reduces to a handful of numbers: the largest and smallest scalar
/// (temperature, displacement magnitude, von Mises stress) and, for
/// displacements, the largest absolute component per axis.
/// </summary>
public sealed class FrdFieldStatistics
{
    #region Constructors

    /// <summary>
    /// Creates empty statistics.
    /// </summary>
    /// <param name="kind">The field kind.</param>
    public FrdFieldStatistics(FrdFieldKind kind)
    {
        Kind = kind;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Takes one row.
    /// </summary>
    /// <param name="values">The row's components (at least 1, 3 or 6 by kind).</param>
    public void Add(double[] values)
    {
        var scalar = Scalar(Kind, values);
        if (Count == 0 || scalar > Max)
            Max = scalar;

        if (Count == 0 || scalar < Min)
            Min = scalar;

        if (Kind == FrdFieldKind.Displacement)
        {
            for (var i = 0; i < 3; i++)
                MaxAbs[i] = System.Math.Max(MaxAbs[i], System.Math.Abs(values[i]));
        }

        Count++;
    }

    /// <summary>
    /// The scalar a field kind reduces a row to.
    /// </summary>
    /// <param name="kind">The field kind.</param>
    /// <param name="values">The row's components.</param>
    /// <returns>Temperature, displacement magnitude or von Mises stress.</returns>
    public static double Scalar(FrdFieldKind kind, double[] values)
    {
        return kind switch
        {
            FrdFieldKind.Displacement => Magnitude(values),
            FrdFieldKind.Stress => VonMises(values),
            _ => values[0]
        };
    }

    /// <summary>
    /// The number of components a field kind needs in a row.
    /// </summary>
    /// <param name="kind">The field kind.</param>
    /// <returns>1, 3 or 6.</returns>
    public static int ComponentsOf(FrdFieldKind kind)
    {
        return kind switch
        {
            FrdFieldKind.Displacement => 3,
            FrdFieldKind.Stress => 6,
            _ => 1
        };
    }

    private static double Magnitude(double[] components)
    {
        return System.Math.Sqrt(
            components[0] * components[0]
            + components[1] * components[1]
            + components[2] * components[2]);
    }

    private static double VonMises(double[] stress)
    {
        var (sxx, syy, szz, sxy, syz, szx) = (stress[0], stress[1], stress[2], stress[3], stress[4], stress[5]);
        return System.Math.Sqrt(0.5 * (
            (sxx - syy) * (sxx - syy)
            + (syy - szz) * (syy - szz)
            + (szz - sxx) * (szz - sxx)
            + 6.0 * (sxy * sxy + syz * syz + szx * szx)));
    }

    #endregion

    #region Properties

    /// <summary>The field kind.</summary>
    public FrdFieldKind Kind { get; }

    /// <summary>Rows taken.</summary>
    public int Count { get; private set; }

    /// <summary>Largest scalar; meaningless while <see cref="Count"/> is 0.</summary>
    public double Max { get; private set; }

    /// <summary>Smallest scalar; meaningless while <see cref="Count"/> is 0.</summary>
    public double Min { get; private set; }

    /// <summary>Largest absolute component per axis (displacements only).</summary>
    public double[] MaxAbs { get; } = new double[3];

    #endregion
}
