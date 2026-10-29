namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// A vector in double precision, for the section-stress geometry: the forces,
/// moments and normals ccx prints for a cut are global vectors.
/// </summary>
public readonly struct Vector3D
{
    #region Constructors

    /// <summary>
    /// Creates the vector.
    /// </summary>
    /// <param name="x">Global x component.</param>
    /// <param name="y">Global y component.</param>
    /// <param name="z">Global z component.</param>
    public Vector3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The scalar product.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>this · other.</returns>
    public double Dot(Vector3D other)
    {
        return X * other.X + Y * other.Y + Z * other.Z;
    }

    /// <summary>
    /// The vector product.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>this × other.</returns>
    public Vector3D Cross(Vector3D other)
    {
        return new Vector3D(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
    }

    /// <summary>
    /// The vector scaled to unit length.
    /// </summary>
    /// <returns>The unit vector; the zero vector stays zero.</returns>
    public Vector3D Unit()
    {
        var length = Length;
        return length > 0.0 ? this * (1.0 / length) : this;
    }

    public static Vector3D operator +(Vector3D a, Vector3D b)
    {
        return new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    public static Vector3D operator -(Vector3D a, Vector3D b)
    {
        return new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    public static Vector3D operator -(Vector3D a)
    {
        return new Vector3D(-a.X, -a.Y, -a.Z);
    }

    public static Vector3D operator *(Vector3D a, double factor)
    {
        return new Vector3D(a.X * factor, a.Y * factor, a.Z * factor);
    }

    public override string ToString()
    {
        return $"({X:G6}, {Y:G6}, {Z:G6})";
    }

    #endregion

    #region Properties

    /// <summary>Global x component.</summary>
    public double X { get; }

    /// <summary>Global y component.</summary>
    public double Y { get; }

    /// <summary>Global z component.</summary>
    public double Z { get; }

    /// <summary>The Euclidean length.</summary>
    public double Length => System.Math.Sqrt(X * X + Y * Y + Z * Z);

    #endregion
}
