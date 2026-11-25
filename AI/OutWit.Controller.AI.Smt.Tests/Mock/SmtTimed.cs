using System.Diagnostics;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A validation and the time it took, for the tests that hold the price of an operation to its
/// work. The texts are read before the clock starts: what is timed is the evaluation alone.
/// </summary>
internal static class SmtTimed
{
    #region Functions

    /// <summary>
    /// Checks a model against a problem, both from text, under the default budget, and says how
    /// long the check took.
    /// </summary>
    /// <param name="problem">The problem's text.</param>
    /// <param name="model">The answer of <c>(get-model)</c>, or null.</param>
    /// <returns>The validation, and the time of the evaluation alone.</returns>
    public static (SmtValidation Validation, TimeSpan Elapsed) Validate(string problem, string? model)
    {
        var validator = new SmtModelValidator(SmtCheck.Commands(problem));
        var read = SmtCheck.Model(model);

        var watch = Stopwatch.StartNew();
        var validation = validator.Validate(read, SmtEvaluationBudget.Default);
        watch.Stop();

        return (validation, watch.Elapsed);
    }

    #endregion
}
