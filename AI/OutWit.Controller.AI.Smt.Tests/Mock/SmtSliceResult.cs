using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// What became of one problem of the slice: the solver's outcome, what the model evaluator made
/// of the model of a satisfiable one, and what the solver made of that model written back into
/// the problem (the closed script).
/// </summary>
internal sealed class SmtSliceResult
{
    #region Properties

    /// <summary>The problem's id in the list.</summary>
    public required string Id { get; init; }

    /// <summary>The problem's logic, as the list names it.</summary>
    public required string Logic { get; init; }

    /// <summary>The verdict the list expects.</summary>
    public required SmtProblemStatus Expected { get; init; }

    /// <summary>The solver's outcome of the problem as a check.</summary>
    public required SmtSolved Outcome { get; init; }

    /// <summary>The evaluator's validation of the model; null when the solver did not say sat.</summary>
    public SmtValidation? Validation { get; init; }

    /// <summary>The symbols the closed script leaves declared: the model did not define them. 0 when no closed script was made.</summary>
    public int ClosedFree { get; init; }

    /// <summary>Why the closed script was not given to the solver (the sanitizer's refusal of it); null when it was, or when none was made.</summary>
    public string? ClosedRefusal { get; init; }

    /// <summary>The solver's outcome of the closed script; null when none was run.</summary>
    public SmtSolved? Closed { get; init; }

    #endregion
}
