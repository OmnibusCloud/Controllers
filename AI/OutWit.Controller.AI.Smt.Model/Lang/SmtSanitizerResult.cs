using System.Diagnostics.CodeAnalysis;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What the sanitizer made of the text of a problem: the problem, or the refusal. Exactly one
/// of the two is there. Immutable.
/// </summary>
public sealed class SmtSanitizerResult
{
    #region Constructors

    internal SmtSanitizerResult(SmtProblem problem)
    {
        Problem = problem;
    }

    internal SmtSanitizerResult(SmtRefusalData refusal)
    {
        Refusal = refusal;
    }

    #endregion

    #region Properties

    /// <summary>The problem; null when the text was refused.</summary>
    public SmtProblem? Problem { get; }

    /// <summary>Why the text was refused; null when it was taken.</summary>
    public SmtRefusalData? Refusal { get; }

    /// <summary>Whether the text was taken.</summary>
    [MemberNotNullWhen(true, nameof(Problem))]
    public bool IsAccepted => Problem != null;

    #endregion
}
