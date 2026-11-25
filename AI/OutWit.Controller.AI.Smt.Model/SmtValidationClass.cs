namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What checking a <c>sat</c> answer against the problem gave: every assertion of the problem
/// evaluated under the model the solver printed, and every value the solver reported with the
/// model held to it.
/// </summary>
public enum SmtValidationClass
{
    /// <summary>
    /// Every assertion evaluated to true, and every reported value is the value its term has
    /// under the model. The model satisfies the problem, so the answer needs no trust in
    /// whoever gave it.
    /// </summary>
    Validated = 0,

    /// <summary>
    /// Neither: no assertion was found false and no reported value another than its term's,
    /// and at least one of them could not be decided to the end (a construct the evaluator does
    /// not close, a symbol the model left out, a budget), or the values are not the answer of
    /// what was asked. The answer rests on other checks.
    /// </summary>
    Partial = 1,

    /// <summary>
    /// An assertion evaluated to false, or a reported value is another than the value its term
    /// has under the model, with nothing guessed. The model does not satisfy the problem, or
    /// does not give the values that came with it: the answer is wrong.
    /// </summary>
    Failed = 2
}
