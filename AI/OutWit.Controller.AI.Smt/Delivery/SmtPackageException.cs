namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// A package a node does not work on, with the code that says why (one of
/// <see cref="Model.SmtNodeFaults"/>) and a sentence in fixed words: versions, sizes and hashes,
/// never a path, an account or an exception's own text, since it travels back in the result
/// package.
/// </summary>
internal sealed class SmtPackageException : Exception
{
    #region Constructors

    /// <summary>
    /// A package the node does not work on.
    /// </summary>
    /// <param name="code">The fault's code.</param>
    /// <param name="message">Why, in fixed words.</param>
    public SmtPackageException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    #endregion

    #region Properties

    /// <summary>The fault's code, one of <see cref="Model.SmtNodeFaults"/>.</summary>
    public string Code { get; }

    #endregion
}
