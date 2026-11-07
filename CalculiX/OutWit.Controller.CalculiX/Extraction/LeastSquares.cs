namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Least squares over cuts of a plane stress state: a symmetric tensor
/// (sxx, syy, sxy) from the tractions t = s · n of cuts with unit normals n,
/// or a vector (qx, qy) from the normal components q = (qx, qy) · n. Two
/// cuts in different directions determine either; more are averaged.
/// </summary>
internal sealed class LeastSquares
{
    #region Fields

    private readonly double[,] m_matrix = new double[3, 3];
    private readonly double[] m_right = new double[3];

    #endregion

    #region Functions

    /// <summary>
    /// Adds the traction of a cut, for <see cref="Tensor"/>.
    /// </summary>
    /// <param name="nx">The cut normal's x component.</param>
    /// <param name="ny">The cut normal's y component.</param>
    /// <param name="tx">The traction's x component.</param>
    /// <param name="ty">The traction's y component.</param>
    public void Add(double nx, double ny, double tx, double ty)
    {
        // tx = sxx nx + sxy ny, ty = sxy nx + syy ny; unknowns (sxx, syy, sxy).
        m_matrix[0, 0] += nx * nx;
        m_matrix[0, 2] += nx * ny;
        m_matrix[1, 1] += ny * ny;
        m_matrix[1, 2] += nx * ny;
        m_matrix[2, 2] += nx * nx + ny * ny;
        m_right[0] += nx * tx;
        m_right[1] += ny * ty;
        m_right[2] += ny * tx + nx * ty;
    }

    /// <summary>
    /// Adds the normal component of a vector on a cut, for <see cref="Vector"/>.
    /// </summary>
    /// <param name="nx">The cut normal's x component.</param>
    /// <param name="ny">The cut normal's y component.</param>
    /// <param name="q">The vector's component along the normal.</param>
    public void AddVector(double nx, double ny, double q)
    {
        m_matrix[0, 0] += nx * nx;
        m_matrix[0, 1] += nx * ny;
        m_matrix[1, 1] += ny * ny;
        m_right[0] += nx * q;
        m_right[1] += ny * q;
    }

    /// <summary>
    /// The tensor that fits the tractions best.
    /// </summary>
    /// <returns>(sxx, syy, sxy); zeros when the cuts do not determine it.</returns>
    public double[] Tensor()
    {
        var a = (double[,])m_matrix.Clone();
        a[2, 0] = a[0, 2];
        a[2, 1] = a[1, 2];
        var b = (double[])m_right.Clone();
        return Solve(a, b);
    }

    /// <summary>
    /// The vector that fits the normal components best.
    /// </summary>
    /// <returns>(qx, qy); zeros when the cuts do not determine it.</returns>
    public (double X, double Y) Vector()
    {
        var (a, b, c) = (m_matrix[0, 0], m_matrix[0, 1], m_matrix[1, 1]);
        var determinant = a * c - b * b;
        if (System.Math.Abs(determinant) <= 1e-12 * System.Math.Max(1.0, a * c))
            return (0.0, 0.0);

        return ((c * m_right[0] - b * m_right[1]) / determinant, (a * m_right[1] - b * m_right[0]) / determinant);
    }

    private static double[] Solve(double[,] a, double[] b)
    {
        // Gaussian elimination with partial pivoting on the 3x3 normal equations.
        var scale = System.Math.Max(1.0, System.Math.Abs(a[2, 2]));
        for (var column = 0; column < 3; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 3; row++)
            {
                if (System.Math.Abs(a[row, column]) > System.Math.Abs(a[pivot, column]))
                    pivot = row;
            }

            if (System.Math.Abs(a[pivot, column]) <= 1e-12 * scale)
                return [0.0, 0.0, 0.0];

            if (pivot != column)
            {
                for (var k = 0; k < 3; k++)
                    (a[column, k], a[pivot, k]) = (a[pivot, k], a[column, k]);

                (b[column], b[pivot]) = (b[pivot], b[column]);
            }

            for (var row = column + 1; row < 3; row++)
            {
                var factor = a[row, column] / a[column, column];
                for (var k = column; k < 3; k++)
                    a[row, k] -= factor * a[column, k];

                b[row] -= factor * b[column];
            }
        }

        var x = new double[3];
        for (var row = 2; row >= 0; row--)
        {
            var sum = b[row];
            for (var k = row + 1; k < 3; k++)
                sum -= a[row, k] * x[k];

            x[row] = sum / a[row, row];
        }

        return x;
    }

    #endregion
}
