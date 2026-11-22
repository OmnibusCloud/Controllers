using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The function under an array whose content is not known as a finite map: a function the
/// model interprets (<c>(_ as-array f)</c>), or the body of a <c>lambda</c> with the local
/// names it was written among. Such an array can be read at an index and cannot be compared.
/// </summary>
internal sealed class SmtArraySource
{
    #region Constructors

    /// <summary>
    /// Creates the source of an array that is a named function.
    /// </summary>
    /// <param name="function">The function.</param>
    public SmtArraySource(SmtFunction function)
    {
        Function = function;
    }

    /// <summary>
    /// Creates the source of an array that is a <c>lambda</c>.
    /// </summary>
    /// <param name="parameter">The name of the lambda's variable.</param>
    /// <param name="body">The lambda's body.</param>
    /// <param name="scope">A copy of the local names the lambda was written among.</param>
    public SmtArraySource(string parameter, SmtExpression body, SmtScope scope)
    {
        Parameter = parameter;
        Body = body;
        Scope = scope;
    }

    #endregion

    #region Properties

    /// <summary>The named function; null for a lambda.</summary>
    public SmtFunction? Function { get; }

    /// <summary>The name of a lambda's variable; null for a named function.</summary>
    public string? Parameter { get; }

    /// <summary>A lambda's body; null for a named function.</summary>
    public SmtExpression? Body { get; }

    /// <summary>A copy of the local names a lambda was written among; null for a named function.</summary>
    public SmtScope? Scope { get; }

    #endregion
}
