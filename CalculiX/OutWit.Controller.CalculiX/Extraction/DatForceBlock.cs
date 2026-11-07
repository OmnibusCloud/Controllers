namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One "forces (fx,fy,fz) for set …" block of a ccx .dat file: the node set,
/// the time ccx printed it at, the per-node rows, and the global total ccx
/// prints after them for <c>*NODE PRINT, TOTALS=YES</c> or <c>TOTALS=ONLY</c>
/// (null when the deck did not ask for it). The total is summed by ccx from
/// the global components before any <c>*TRANSFORM</c> is applied to the rows.
/// </summary>
public sealed class DatForceBlock
{
    #region Constructors

    /// <summary>
    /// Creates the block.
    /// </summary>
    /// <param name="setName">Node set the block was printed for.</param>
    /// <param name="time">Time the block was printed at.</param>
    public DatForceBlock(string setName, double time)
    {
        SetName = setName;
        Time = time;
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"forces for set {SetName} at {Time}: {Rows.Count} row(s){(Total == null ? "" : ", total")}";
    }

    #endregion

    #region Properties

    /// <summary>Node set the forces were printed for.</summary>
    public string SetName { get; }

    /// <summary>Time the block was printed at.</summary>
    public double Time { get; }

    /// <summary>Per-node rows in file order; empty for <c>TOTALS=ONLY</c>.</summary>
    public List<DatForceRow> Rows { get; } = [];

    /// <summary>The global total ccx printed (x, y, z), or null when the deck did not ask for it.</summary>
    public double[]? Total { get; set; }

    #endregion
}
