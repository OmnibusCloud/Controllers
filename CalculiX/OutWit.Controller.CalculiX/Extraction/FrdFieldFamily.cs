namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One field of one step as the accumulator follows it through the .frd:
/// the running extremes of the latest block (the step's final state once
/// the file ends), the peak over every block of the step, the probe samples
/// of the latest block, and the first broken row of the latest block.
/// </summary>
internal sealed class FrdFieldFamily
{
    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="kind">The field kind.</param>
    public FrdFieldFamily(FrdFieldKind kind)
    {
        Kind = kind;
        Last = new FrdFieldStatistics(kind);
    }

    #endregion

    #region Functions

    /// <summary>
    /// A new block of the field begins: it replaces the earlier one as the
    /// step's final state.
    /// </summary>
    /// <param name="header">The block's header.</param>
    public void Begin(FrdBlockHeader header)
    {
        Last = new FrdFieldStatistics(Kind);
        Samples.Clear();
        Broken = null;
        Blocks++;
        LastIsModeShape = header.IsModeShape;
        LastMode = header.Mode;
    }

    #endregion

    #region Properties

    /// <summary>The field kind.</summary>
    public FrdFieldKind Kind { get; }

    /// <summary>Running extremes of the latest block.</summary>
    public FrdFieldStatistics Last { get; private set; }

    /// <summary>Result node → scalar, for the nodes the probes ask for, latest block.</summary>
    public Dictionary<int, double> Samples { get; } = [];

    /// <summary>The first row of the latest block the reader could not take; null when none.</summary>
    public (int Node, string Text)? Broken { get; set; }

    /// <summary>Number of blocks of the field seen in the step.</summary>
    public int Blocks { get; private set; }

    /// <summary>Largest scalar over every block of the step.</summary>
    public double AnyBlockMax { get; set; }

    /// <summary>True when the latest block is a mode shape.</summary>
    public bool LastIsModeShape { get; private set; }

    /// <summary>Mode number of the latest block; 0 for a non-modal block.</summary>
    public int LastMode { get; private set; }

    #endregion
}
