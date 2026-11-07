namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What one step of a ccx .dat file printed: its eigenvalue tables,
/// buckling factors and reaction-force blocks (the last block per set - the
/// final state of the step), plus what was seen and left out: the forces
/// ccx prints per mode after an eigenvalue solve are mode shapes, not
/// reactions, and an eigenvalue table in a layout the reader does not know
/// yields no frequency rather than a wrong one.
/// </summary>
public sealed class DatStepResult
{
    #region Constructors

    /// <summary>
    /// Creates the step record.
    /// </summary>
    /// <param name="number">1-based step number.</param>
    public DatStepResult(int number)
    {
        Number = number;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Records a forces block: a later block for the same set replaces the
    /// earlier one (ccx prints one per increment; the response is the final state).
    /// </summary>
    /// <param name="block">The block.</param>
    public void AddForces(DatForceBlock block)
    {
        var earlier = ForceBlocks.FindIndex(existing => existing.SetName.Equals(block.SetName, StringComparison.OrdinalIgnoreCase));
        if (earlier >= 0)
            ForceBlocks[earlier] = block;
        else
            ForceBlocks.Add(block);
    }

    /// <summary>
    /// Records a section-print block: a later block for the same surface
    /// replaces the earlier one (ccx prints one per increment).
    /// </summary>
    /// <param name="block">The block.</param>
    public void AddSection(DatSectionBlock block)
    {
        Sections[block.SetName] = block;
    }

    public override string ToString()
    {
        return $"step {Number}: {Eigenfrequencies.Count} eigenfrequencies, {BucklingFactors.Count} buckling factors, {ForceBlocks.Count} force block(s)";
    }

    #endregion

    #region Properties

    /// <summary>1-based step number.</summary>
    public int Number { get; }

    /// <summary>Eigenfrequencies in table order.</summary>
    public List<DatEigenfrequency> Eigenfrequencies { get; } = [];

    /// <summary>Number of eigenvalue tables whose layout the reader did not recognize.</summary>
    public int UnknownEigenvalueTables { get; set; }

    /// <summary>Buckling factors in mode order.</summary>
    public List<double> BucklingFactors { get; } = [];

    /// <summary>Reaction-force blocks, the last one per printed set.</summary>
    public List<DatForceBlock> ForceBlocks { get; } = [];

    /// <summary>Section-print blocks, the last one per surface, by surface name (case-insensitive).</summary>
    public Dictionary<string, DatSectionBlock> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of forces blocks printed per mode after an eigenvalue solve and left out.</summary>
    public int ModeShapeForceBlocks { get; set; }

    #endregion
}
