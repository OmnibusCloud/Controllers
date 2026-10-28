namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One nodal result block of a ccx .frd file held in memory: the dataset
/// name (NDTEMP, DISP, STRESS, …), its step and step time, the stored
/// component names and the per-node component values. Calculated components
/// (the DISP "ALL" magnitude) are not stored in the file and are not listed
/// here. The extractor streams blocks instead (<see cref="IFrdResultVisitor"/>);
/// this is the in-memory form for small files and tests.
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
        : this(new FrdBlockHeader(name, 1, 1, stepTime, ' ', 0))
    {
    }

    /// <summary>
    /// Creates an empty block under the given header.
    /// </summary>
    /// <param name="header">The block's header; its component list is shared.</param>
    public FrdResultBlock(FrdBlockHeader header)
    {
        Header = header;
    }

    #endregion

    #region Properties

    /// <summary>The block's header: dataset, step, time, components.</summary>
    public FrdBlockHeader Header { get; }

    /// <summary>Dataset name, e.g. "NDTEMP", "DISP", "STRESS".</summary>
    public string Name => Header.Name;

    /// <summary>1-based step number of the block.</summary>
    public int Step => Header.Step;

    /// <summary>Step time of the enclosing result step.</summary>
    public double StepTime => Header.Time;

    /// <summary>Stored component names in file order (calculated ones excluded).</summary>
    public List<string> Components => Header.Components;

    /// <summary>Per-node stored component values.</summary>
    public Dictionary<int, double[]> Values { get; } = [];

    #endregion
}
