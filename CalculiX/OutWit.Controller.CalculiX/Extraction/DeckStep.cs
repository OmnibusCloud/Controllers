namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One <c>*STEP</c> of a deck: its 1-based number (the number ccx writes
/// into the .frd and .dat) and its procedure card.
/// </summary>
public sealed class DeckStep
{
    #region Constants

    private static readonly HashSet<string> EIGENVALUE_PROCEDURES = new(StringComparer.Ordinal)
    {
        "FREQUENCY",
        "BUCKLE",
        "COMPLEXFREQUENCY"
    };

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="number">1-based step number.</param>
    /// <param name="procedure">Normalized procedure keyword ("STATIC", "FREQUENCY", "HEATTRANSFER", ...), or null when the step names none.</param>
    public DeckStep(int number, string? procedure)
    {
        Number = number;
        Procedure = procedure;
    }

    #endregion

    #region Functions

    public override string ToString()
    {
        return $"step {Number}: *{Procedure ?? "?"}";
    }

    #endregion

    #region Properties

    /// <summary>1-based step number.</summary>
    public int Number { get; }

    /// <summary>Normalized procedure keyword, or null when the step names none.</summary>
    public string? Procedure { get; }

    /// <summary>
    /// True for a step that solves an eigenvalue problem: its .frd blocks are
    /// mode shapes and the forces ccx prints per mode are mode shapes too.
    /// </summary>
    public bool IsEigenvalue => Procedure != null && EIGENVALUE_PROCEDURES.Contains(Procedure);

    #endregion
}
