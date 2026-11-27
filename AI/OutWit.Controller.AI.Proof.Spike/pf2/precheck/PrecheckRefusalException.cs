namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// The pre-check refuses an export: the first rule it breaks, with the line it was found on.
/// </summary>
public sealed class PrecheckRefusalException : Exception
{
    #region Constructors

    /// <summary>
    /// Creates a refusal.
    /// </summary>
    /// <param name="code">The refusal code of spec section 5.4 (<c>meta_invalid</c>, <c>index_invalid</c>, ...).</param>
    /// <param name="detail">What was found.</param>
    public PrecheckRefusalException(string code, string detail)
        : base($"{code}: {detail}")
    {
        Code = code;
        Detail = detail;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The refusal code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// What was found.
    /// </summary>
    public string Detail { get; }

    #endregion
}
