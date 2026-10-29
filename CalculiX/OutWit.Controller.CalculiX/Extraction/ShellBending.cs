namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The bending part of the stress at a shell node: the shell normal and the
/// in-plane stress that the bending moment adds on the top face (and takes
/// off on the bottom one), 6M/t², as a global tensor. Added to the
/// membrane stress the .frd carries at the node under <c>OUTPUT=2D</c> - the
/// mean through the thickness, which is the whole stress of a membrane - it
/// gives the stress of both faces.
/// </summary>
public sealed class ShellBending
{
    #region Constructors

    /// <summary>
    /// Creates the node's bending part.
    /// </summary>
    /// <param name="normal">The unit shell normal.</param>
    /// <param name="bending">The face stress of the bending moment: xx, yy, zz, xy, yz, zx, global.</param>
    public ShellBending(Vector3D normal, double[] bending)
    {
        Normal = normal;
        Bending = bending;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The largest von Mises stress through the thickness: of the top face,
    /// the bottom face, and the mid-surface, where the transverse shear peaks
    /// at 1.5 times its mean.
    /// </summary>
    /// <param name="membrane">
    /// The node's mean stress through the thickness, xx, yy, zz, xy, yz, zx
    /// (the order of the .frd STRESS block), global.
    /// </param>
    /// <returns>The von Mises stress.</returns>
    public double VonMises(double[] membrane)
    {
        // The transverse shear is the part of the traction on the shell's
        // own plane that lies in that plane; the faces carry none of it.
        var (nx, ny, nz) = (Normal.X, Normal.Y, Normal.Z);
        var tx = membrane[0] * nx + membrane[3] * ny + membrane[5] * nz;
        var ty = membrane[3] * nx + membrane[1] * ny + membrane[4] * nz;
        var tz = membrane[5] * nx + membrane[4] * ny + membrane[2] * nz;
        var normalStress = tx * nx + ty * ny + tz * nz;
        var (sx, sy, sz) = (tx - normalStress * nx, ty - normalStress * ny, tz - normalStress * nz);
        double[] shear = [2.0 * sx * nx, 2.0 * sy * ny, 2.0 * sz * nz, sx * ny + sy * nx, sy * nz + sz * ny, sz * nx + sx * nz];

        var top = new double[6];
        var bottom = new double[6];
        var middle = new double[6];
        for (var i = 0; i < 6; i++)
        {
            var plane = membrane[i] - shear[i];
            top[i] = plane + Bending[i];
            bottom[i] = plane - Bending[i];
            middle[i] = plane + 1.5 * shear[i];
        }

        return System.Math.Max(System.Math.Max(FrdFieldStatistics.Scalar(FrdFieldKind.Stress, top),
            FrdFieldStatistics.Scalar(FrdFieldKind.Stress, bottom)), FrdFieldStatistics.Scalar(FrdFieldKind.Stress, middle));
    }

    public override string ToString()
    {
        return $"normal {Normal}, bending [{string.Join(", ", Bending.Select(value => value.ToString("G6")))}]";
    }

    #endregion

    #region Properties

    /// <summary>The unit shell normal.</summary>
    public Vector3D Normal { get; }

    /// <summary>The face stress of the bending moment: xx, yy, zz, xy, yz, zx, global.</summary>
    public double[] Bending { get; }

    #endregion
}
