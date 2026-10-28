namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What the extractor takes from a ccx .dat file, step by step:
/// eigenfrequencies, buckling factors and reaction-force blocks. Everything
/// is optional — most decks print nothing and leave the file empty.
/// </summary>
public sealed class DatResultFile
{
    #region Functions

    /// <summary>
    /// The record of a step, created on first use.
    /// </summary>
    /// <param name="number">1-based step number.</param>
    /// <returns>The step's record.</returns>
    public DatStepResult Step(int number)
    {
        var step = Steps.Find(candidate => candidate.Number == number);
        if (step == null)
        {
            step = new DatStepResult(number);
            Steps.Add(step);
        }

        return step;
    }

    #endregion

    #region Properties

    /// <summary>Steps that printed anything, in file order.</summary>
    public List<DatStepResult> Steps { get; } = [];

    #endregion
}
