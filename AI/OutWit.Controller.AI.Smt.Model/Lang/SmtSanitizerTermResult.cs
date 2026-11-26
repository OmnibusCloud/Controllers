using System.Diagnostics.CodeAnalysis;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What the sanitizer made of the text of one term: the term, or the refusal. Exactly one of
/// the two is there. Immutable.
/// </summary>
public sealed class SmtSanitizerTermResult
{
    #region Constructors

    internal SmtSanitizerTermResult(SmtExpression term)
    {
        Term = term;
        IsAccepted = true;
    }

    internal SmtSanitizerTermResult(SmtRefusalData refusal)
    {
        Refusal = refusal;
    }

    #endregion

    #region Properties

    /// <summary>The term; null when the text was refused.</summary>
    public SmtExpression? Term { get; }

    /// <summary>Why the text was refused; null when it was taken.</summary>
    public SmtRefusalData? Refusal { get; }

    /// <summary>Whether the text was taken: then the term is there, else the refusal (the constructors make it so).</summary>
    [MemberNotNullWhen(true, nameof(Term))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsAccepted { get; }

    #endregion
}
