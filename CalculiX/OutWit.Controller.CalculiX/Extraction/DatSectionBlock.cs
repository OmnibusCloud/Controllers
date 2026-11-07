namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One "statistics for surface set …" block of a ccx .dat file, which a
/// <c>*SECTION PRINT</c> with <c>SOF</c> prints (printoutface.f): the force
/// that acts on the surface's elements through the surface (the stress
/// integrated over the faces with their outward normals), the moment of
/// that force about the surface's centre of gravity, the centre, the mean
/// outward normal and the area - all global, in the deformed geometry under
/// NLGEOM.
/// </summary>
public sealed class DatSectionBlock
{
    #region Constructors

    /// <summary>
    /// Creates the block.
    /// </summary>
    /// <param name="setName">The surface name, as ccx prints it.</param>
    /// <param name="time">The total time of the increment.</param>
    /// <param name="force">The total force.</param>
    /// <param name="centre">The centre of gravity.</param>
    /// <param name="normal">The mean normal.</param>
    /// <param name="moment">The moment about the centre of gravity.</param>
    /// <param name="area">The area.</param>
    public DatSectionBlock(string setName, double time, Vector3D force, Vector3D centre, Vector3D normal, Vector3D moment, double area)
    {
        SetName = setName;
        Time = time;
        Force = force;
        Centre = centre;
        Normal = normal;
        Moment = moment;
        Area = area;
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"{SetName} t={Time}: F={Force} M={Moment} A={Area:G6}";
    }

    #endregion

    #region Properties

    /// <summary>The surface name, as ccx prints it.</summary>
    public string SetName { get; }

    /// <summary>The total time of the increment.</summary>
    public double Time { get; }

    /// <summary>The total force on the surface's elements through the surface.</summary>
    public Vector3D Force { get; }

    /// <summary>The centre of gravity of the surface.</summary>
    public Vector3D Centre { get; }

    /// <summary>The mean outward normal of the surface.</summary>
    public Vector3D Normal { get; }

    /// <summary>The moment of the force about the centre of gravity.</summary>
    public Vector3D Moment { get; }

    /// <summary>The area of the surface.</summary>
    public double Area { get; }

    #endregion
}
