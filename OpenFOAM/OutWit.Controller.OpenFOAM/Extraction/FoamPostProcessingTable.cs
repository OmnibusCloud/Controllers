namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// The last row of a function-object data file as <see cref="FoamPostProcessingReader"/>
/// reads it: the column names, the values (a column left out is
/// <see cref="double.NaN"/>), one sentence per value left out and why, and
/// whether the file is a probes file - whose column names carry the field
/// already, so a response names its values without the file's name in between.
/// </summary>
public sealed class FoamPostProcessingTable
{
    #region Constructors

    /// <summary>
    /// A table.
    /// </summary>
    /// <param name="columns">The column names, the time column first when the file has one.</param>
    /// <param name="values">The values of the last row, one per column.</param>
    /// <param name="notes">One sentence per value left out.</param>
    /// <param name="isProbes">True for a probes file.</param>
    public FoamPostProcessingTable(IReadOnlyList<string> columns, IReadOnlyList<double> values, IReadOnlyList<string> notes, bool isProbes)
    {
        Columns = columns;
        Values = values;
        Notes = notes;
        IsProbes = isProbes;
    }

    #endregion

    #region Properties

    /// <summary>The column names, as many as there are values.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The values of the last row; <see cref="double.NaN"/> for a value that is not a number or was left out.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>Why a value was left out, one sentence each; empty when every value stands.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    /// Whether the file is a probes file: its columns are named
    /// <c>&lt;field&gt;@probe&lt;N&gt;(x y z)</c> with a component suffix, which
    /// carries the field, so the file's name is not repeated in a value's name.
    /// </summary>
    public bool IsProbes { get; }

    #endregion
}
