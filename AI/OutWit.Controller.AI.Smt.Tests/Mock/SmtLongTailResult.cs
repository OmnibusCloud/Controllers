using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// What became of one problem of the long-tail kit's sample: the solver's outcome and the
/// evaluator's class of its model, or why it was not solved.
/// </summary>
internal sealed class SmtLongTailResult
{
    #region Properties

    /// <summary>The problem's row of the list.</summary>
    public required SmtLongTailRow Row { get; init; }

    /// <summary>The solver's outcome; null when the problem was not solved (<see cref="Fault"/>).</summary>
    public SmtSolved? Outcome { get; init; }

    /// <summary>The evaluator's validation of the model; null without a model.</summary>
    public SmtValidation? Validation { get; init; }

    /// <summary>Why the problem was not solved: its file is not there or not the listed one, or the sanitizer refused it; null when it was.</summary>
    public string? Fault { get; init; }

    #endregion
}
