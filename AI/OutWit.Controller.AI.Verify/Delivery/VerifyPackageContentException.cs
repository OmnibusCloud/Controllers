namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// A result package that can be read but does not hold what the task package it answers asked
/// for: more batches or more results than that package had. Told apart from a package that
/// cannot be read at all, since the documents count the two under different reasons.
/// </summary>
public sealed class VerifyPackageContentException : Exception
{
    #region Constructors

    /// <summary>
    /// A package with content it may not have.
    /// </summary>
    /// <param name="message">What is wrong, in fixed words.</param>
    public VerifyPackageContentException(string message)
        : base(message)
    {
    }

    #endregion
}
