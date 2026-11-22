using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Shortcuts of the evaluator's tests: a problem and a model from text to a validation.
/// </summary>
internal static class SmtCheck
{
    #region Functions

    /// <summary>
    /// Reads a problem's text into its commands, under the limits of a submitted problem.
    /// </summary>
    /// <param name="problem">The problem's text.</param>
    /// <returns>The commands.</returns>
    public static IReadOnlyList<SmtExpression> Commands(string problem)
    {
        return SmtReader.Read(problem, SmtReaderLimits.Problem);
    }

    /// <summary>
    /// Reads the answers of a solver into a model, without limits of the reader's own.
    /// </summary>
    /// <param name="model">The answer of <c>(get-model)</c>, or null.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>, or null.</param>
    /// <returns>The model.</returns>
    public static SmtModel Model(string? model, string? values = null)
    {
        return SmtModel.Read(model, values, SmtReaderLimits.Unbounded);
    }

    /// <summary>
    /// Checks a model against a problem, both from text.
    /// </summary>
    /// <param name="problem">The problem's text.</param>
    /// <param name="model">The answer of <c>(get-model)</c>, or null.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>, or null.</param>
    /// <param name="budget">The budget; the default one when null.</param>
    /// <returns>The validation.</returns>
    public static SmtValidation Validate(string problem, string? model, string? values = null, SmtEvaluationBudget? budget = null)
    {
        return new SmtModelValidator(Commands(problem)).Validate(Model(model, values), budget ?? SmtEvaluationBudget.Default);
    }

    /// <summary>
    /// Checks the values of a <c>get-value</c> answer against a problem.
    /// </summary>
    /// <param name="problem">The problem's text.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>.</param>
    /// <returns>The validation.</returns>
    public static SmtValidation ValidateValues(string problem, string values)
    {
        return Validate(problem, null, values);
    }

    /// <summary>
    /// Evaluates one closed term of sort Bool: the class says true (validated), false (failed)
    /// or neither (partial, with the reason).
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="declarations">Commands ahead of the assertion, or nothing.</param>
    /// <returns>The validation of <c>(assert term)</c> under the empty model.</returns>
    public static SmtValidation Holds(string term, string declarations = "")
    {
        return Validate($"{declarations}(assert {term})", null);
    }

    /// <summary>
    /// Evaluates one closed term to a value, on the caller's thread: for the tables of the
    /// operators, where the value itself is compared.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="declarations">Commands the term needs, or nothing.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The term is not evaluated to a value.</exception>
    public static SmtValue Evaluate(string term, string declarations = "")
    {
        var problem = SmtProblemReader.Read(Commands(declarations));
        var run = new SmtRun(problem, Model(null), SmtEvaluationBudget.Default);
        return run.Evaluator.Eval(Commands(term).Single(), null, 1);
    }

    /// <summary>
    /// The reason a closed term is not evaluated for.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <param name="declarations">Commands the term needs, or nothing.</param>
    /// <returns>The reason code.</returns>
    public static string ReasonOf(string term, string declarations = "")
    {
        return Assert.Throws<SmtPartialException>(() => Evaluate(term, declarations))!.Reason;
    }

    #endregion
}
