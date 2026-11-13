namespace OutWit.Controller.AI.Corpus.PdfSpike.Guards;

/// <summary>A document passed one of its <see cref="PdfDecodeBudget"/> caps.</summary>
public sealed class PdfBudgetExceededException : Exception
{
    #region Constructors

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which cap was passed.</param>
    public PdfBudgetExceededException(string message) : base(message)
    {
    }

    #endregion
}
