namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One nodal result block of a ccx .frd file: the dataset name (NDTEMP, DISP,
/// STRESS, …), its step time, the stored component names and the per-node
/// component values. Calculated components (the DISP "ALL" magnitude) are not
/// stored in the file and are not listed here.
/// </summary>
public sealed class FrdResultBlock
{
    #region Constructors

    /// <summary>
    /// Creates an empty block for the reader to fill.
    /// </summary>
    /// <param name="name">Dataset name from the -4 record.</param>
    /// <param name="stepTime">Step time from the enclosing 100CL record.</param>
    public FrdResultBlock(string name, double stepTime)
    {
        Name = name;
        StepTime = stepTime;
    }

    #endregion

    #region Properties

    /// <summary>Dataset name, e.g. "NDTEMP", "DISP", "STRESS".</summary>
    public string Name { get; }

    /// <summary>Step time of the enclosing result step.</summary>
    public double StepTime { get; }

    /// <summary>Stored component names in file order (calculated ones excluded).</summary>
    public List<string> Components { get; } = [];

    /// <summary>Per-node stored component values.</summary>
    public Dictionary<int, double[]> Values { get; } = [];

    #endregion
}
