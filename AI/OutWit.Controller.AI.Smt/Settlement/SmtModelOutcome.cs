using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What the host's evaluator said of one model of a row, and what it cost. Copies of a row with
/// one digest are one text: a model is checked once for all of them, and each copy's package pays
/// for it (<see cref="SmtPackageBudget"/>).
/// </summary>
/// <param name="Class">The class of the model, in the word a row writes: <see cref="SmtRunWords.VALIDATED"/>, <see cref="SmtRunWords.PARTIAL"/> or <see cref="SmtRunWords.FAILED"/>.</param>
/// <param name="Reason">The evaluator's reason of a partial or a failed model; empty for a validated one.</param>
/// <param name="Detail">For a failed model, which assertion or which value, and which model of an enumeration; empty otherwise.</param>
/// <param name="Steps">What the model cost (<see cref="SmtPackageBudget.Price"/>).</param>
/// <param name="Fault">Whether checking the model threw: the row is then partial with the evaluator's fault, whatever its other models say.</param>
internal readonly record struct SmtModelOutcome(string Class, string Reason, string Detail, long Steps, bool Fault);
