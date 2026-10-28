namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One row of an eigenvalue table of a ccx .dat file: the mode number, the
/// nodal diameter for a cyclic-symmetry table (null otherwise) and the
/// frequency in cycles per time - the column ccx labels CYCLES/TIME in every
/// one of its four table layouts.
/// </summary>
public sealed class DatEigenfrequency
{
    #region Constructors

    /// <summary>
    /// Creates the row.
    /// </summary>
    /// <param name="mode">Mode number within its table.</param>
    /// <param name="nodalDiameter">Nodal diameter of a cyclic-symmetry table; null otherwise.</param>
    /// <param name="cycles">Frequency in cycles per time.</param>
    public DatEigenfrequency(int mode, int? nodalDiameter, double cycles)
    {
        Mode = mode;
        NodalDiameter = nodalDiameter;
        Cycles = cycles;
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return NodalDiameter == null ? $"mode {Mode}: {Cycles}" : $"nd {NodalDiameter} mode {Mode}: {Cycles}";
    }

    #endregion

    #region Properties

    /// <summary>Mode number within its table (ccx restarts it per nodal diameter).</summary>
    public int Mode { get; }

    /// <summary>Nodal diameter of a cyclic-symmetry table; null for the other layouts.</summary>
    public int? NodalDiameter { get; }

    /// <summary>Frequency in cycles per time.</summary>
    public double Cycles { get; }

    #endregion
}
