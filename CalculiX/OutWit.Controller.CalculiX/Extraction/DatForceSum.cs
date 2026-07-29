namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Reaction-force sum of one printed node set: the per-axis totals of a
/// "forces (fx,fy,fz) for set …" block of a ccx .dat file.
/// </summary>
public sealed class DatForceSum
{
    #region Constructors

    /// <summary>
    /// Creates the sum record.
    /// </summary>
    /// <param name="setName">Node set the block was printed for.</param>
    public DatForceSum(string setName)
    {
        SetName = setName;
    }

    #endregion

    #region Properties

    /// <summary>Node set the forces were printed for.</summary>
    public string SetName { get; }

    /// <summary>Sum of the x components.</summary>
    public double Fx { get; set; }

    /// <summary>Sum of the y components.</summary>
    public double Fy { get; set; }

    /// <summary>Sum of the z components.</summary>
    public double Fz { get; set; }

    #endregion
}
