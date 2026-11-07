namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// The header of one nodal result block of a ccx .frd file: the dataset
/// (NDTEMP, DISP, STRESS, …), the step and increment the block belongs to
/// and its time (the 1PSTEP and 100CL records before it), the mode number
/// of an eigenvalue block (1PMODE), the analysis-type digit of the 100CL
/// record, and the stored component names (calculated components such as
/// the DISP "ALL" magnitude are not stored in the file and are not listed).
/// </summary>
public sealed class FrdBlockHeader
{
    #region Constants

    /// <summary>The 100CL analysis-type digit of a static block.</summary>
    public const char ANALYSIS_STATIC = '0';

    /// <summary>The 100CL analysis-type digit of a frequency (mode shape) block.</summary>
    public const char ANALYSIS_FREQUENCY = '2';

    /// <summary>The 100CL analysis-type digit of a buckling (mode shape) block.</summary>
    public const char ANALYSIS_BUCKLING = '4';

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the header.
    /// </summary>
    /// <param name="name">Dataset name from the -4 record.</param>
    /// <param name="step">1-based step number from the 1PSTEP record; 1 when the file carries none.</param>
    /// <param name="increment">Increment number from the 1PSTEP record.</param>
    /// <param name="time">Step time from the 100CL record.</param>
    /// <param name="analysisType">The analysis-type digit of the 100CL record.</param>
    /// <param name="mode">Mode number from the 1PMODE record; 0 for a non-modal block.</param>
    public FrdBlockHeader(string name, int step, int increment, double time, char analysisType, int mode)
    {
        Name = name;
        Step = step;
        Increment = increment;
        Time = time;
        AnalysisType = analysisType;
        Mode = mode;
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"{Name} step {Step} t={Time} [{string.Join(",", Components)}]";
    }

    #endregion

    #region Properties

    /// <summary>Dataset name, e.g. "NDTEMP", "DISP", "STRESS".</summary>
    public string Name { get; }

    /// <summary>1-based step number.</summary>
    public int Step { get; }

    /// <summary>Increment number within the step.</summary>
    public int Increment { get; }

    /// <summary>Step time of the block.</summary>
    public double Time { get; }

    /// <summary>The analysis-type digit of the 100CL record ('0' static, '2' frequency, '4' buckling, ...).</summary>
    public char AnalysisType { get; }

    /// <summary>Mode number of an eigenvalue block; 0 otherwise.</summary>
    public int Mode { get; }

    /// <summary>True for a mode-shape block of a frequency or buckling solve.</summary>
    public bool IsModeShape => AnalysisType is ANALYSIS_FREQUENCY or ANALYSIS_BUCKLING;

    /// <summary>Stored component names in file order (calculated ones excluded).</summary>
    public List<string> Components { get; } = [];

    #endregion
}
