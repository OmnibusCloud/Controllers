namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// What one row of the results has spent of its budget of models (<see cref="SmtResultsWriter.MAX_MODEL_BYTES"/>)
/// while its texts are written, and whether a text was left out for it.
/// </summary>
internal sealed class SmtResultsBudget
{
    #region Properties

    /// <summary>The bytes of UTF-8 of the texts written so far.</summary>
    public long Spent { get; set; }

    /// <summary>Whether a text did not fit: it and every text after it are written as null.</summary>
    public bool Cut { get; set; }

    #endregion
}
