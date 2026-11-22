namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What checking a <c>sat</c> answer against the problem gave: every assertion of the problem
/// evaluated under the model the solver printed.
/// </summary>
public enum SmtValidationClass
{
    /// <summary>
    /// Every assertion evaluated to true. The model satisfies the problem, so the answer needs
    /// no trust in whoever gave it.
    /// </summary>
    Validated = 0,

    /// <summary>
    /// Neither: no assertion was found false, and at least one could not be evaluated to the
    /// end (a construct the evaluator does not close, a symbol the model left out, a budget).
    /// The answer rests on other checks.
    /// </summary>
    Partial = 1,

    /// <summary>
    /// An assertion evaluated to false with nothing guessed. The model does not satisfy the
    /// problem: the answer is wrong.
    /// </summary>
    Failed = 2
}
