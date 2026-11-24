namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>A local run that cannot go on, with the reason in the tool's words: no sandbox, a set without a row, not one session in the memory.</summary>
internal sealed class SmtLiveLocalException : Exception
{
    #region Constructors

    /// <summary>Makes the exception.</summary>
    /// <param name="message">The reason.</param>
    public SmtLiveLocalException(string message)
        : base(message)
    {
    }

    #endregion
}
