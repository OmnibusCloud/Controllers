namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What the extractor takes from a ccx .dat file: eigenfrequencies, buckling
/// factors and reaction-force sums. Everything is optional — most decks print
/// nothing and leave the file empty.
/// </summary>
public sealed class DatResultFile
{
    #region Properties

    /// <summary>Eigenfrequencies in cycles per time, in mode order.</summary>
    public List<double> Eigenfrequencies { get; } = [];

    /// <summary>Buckling factors in mode order.</summary>
    public List<double> BucklingFactors { get; } = [];

    /// <summary>Reaction-force sums, one per printed node set.</summary>
    public List<DatForceSum> ForceSums { get; } = [];

    #endregion
}
