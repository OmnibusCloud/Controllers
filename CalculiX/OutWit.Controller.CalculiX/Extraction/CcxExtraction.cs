using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// What one extraction produced: the response row, or none when the
/// extraction failed (the notes then carry an <see cref="CcxNoteKind.Error"/>
/// note saying why), and the notes about what was changed, refused, rotated
/// or could not be read.
/// </summary>
public sealed class CcxExtraction
{
    #region Constructors

    /// <summary>
    /// Creates the outcome.
    /// </summary>
    /// <param name="row">The response row; null when the extraction failed.</param>
    /// <param name="notes">The notes, in extraction order.</param>
    public CcxExtraction(CcxResponseRowData? row, List<CcxNoteData> notes)
    {
        Row = row;
        Notes = notes;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The outcome of an extraction that failed as a whole.
    /// </summary>
    /// <param name="reason">Why, for the person reading the results.</param>
    /// <param name="notes">Notes collected before the failure.</param>
    /// <returns>An outcome without a row and with the error note last.</returns>
    public static CcxExtraction Failure(string reason, List<CcxNoteData> notes)
    {
        notes.Add(new CcxNoteData { Kind = CcxNoteKind.Error, Text = reason });
        return new CcxExtraction(null, notes);
    }

    #endregion

    #region Properties

    /// <summary>The response row; null when the extraction failed.</summary>
    public CcxResponseRowData? Row { get; }

    /// <summary>The notes, in extraction order.</summary>
    public List<CcxNoteData> Notes { get; }

    /// <summary>True when the extraction failed and the row is missing.</summary>
    public bool Failed => Row == null;

    #endregion
}
