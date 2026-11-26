using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What the host's evaluator has spent on the models of one result package. One check of a model
/// has a budget of its own (<see cref="SmtEvaluationBudget"/>); a package has this one over all
/// its models together, so what a node can make the host spend is bounded by the packages it
/// returns, whatever its rows hold. A model pays what it took: the evaluator's steps, its text
/// (read before a step is counted) and the work of starting its check
/// (<see cref="Price(SmtValidation, long)"/>).
/// <para>
/// A package spends its budget on its rows in their order, a model at a time, and only on its own
/// rows: what it has left before a row follows from its earlier rows alone, so every activity that
/// checks the rows of a round sees the same checks. Once a package has used its budget up, the
/// rest of its models are not checked: the row is partial (<see cref="SmtRunWords.BUDGET_PACKAGE"/>)
/// and its answer rests on the copies. The model that takes a package past its budget is checked
/// to its end: a package spends at most its budget and one model's price.
/// </para>
/// </summary>
internal sealed class SmtPackageBudget
{
    #region Constants

    /// <summary>The steps the models of one result package may cost the host together.</summary>
    public const long DEFAULT_STEPS = 1_000_000_000;

    /// <summary>What one model pays besides its steps: the thread its check runs on, the model's reading, the check's setting up.</summary>
    public const long MODEL_STEPS = 10_000;

    /// <summary>The characters of a model's text, and of the values beside it, that one step pays for reading.</summary>
    public const int CHARACTERS_PER_STEP = 1;

    #endregion

    #region Fields

    private long m_spent;

    #endregion

    #region Functions

    /// <summary>What one model cost: its steps, its text and the setting up of its check.</summary>
    /// <param name="validation">What the check gave; null when the check did not end (it threw).</param>
    /// <param name="characters">The characters of the model and of its values.</param>
    /// <returns>The steps.</returns>
    public static long Price(SmtValidation? validation, long characters)
    {
        // A check that threw, or that ended in a fault of the evaluator, may have spent its whole
        // budget before it did: it pays the whole budget.
        var steps = validation == null || validation.Reason == SmtValidationReasons.EVALUATOR_ERROR ? SmtEvaluationBudget.DEFAULT_STEPS : Math.Max(0, validation.Steps);
        return MODEL_STEPS + Math.Max(0, characters) / CHARACTERS_PER_STEP + steps;
    }

    /// <summary>Whether the package has steps left under a budget.</summary>
    /// <param name="maxSteps">The budget.</param>
    /// <returns>True while the package has spent less than the budget.</returns>
    public bool HasRoom(long maxSteps)
    {
        return m_spent < maxSteps;
    }

    /// <summary>Pays for one model.</summary>
    /// <param name="steps">Its price.</param>
    public void Charge(long steps)
    {
        m_spent = steps >= long.MaxValue - m_spent ? long.MaxValue : m_spent + Math.Max(0, steps);
    }

    #endregion

    #region Properties

    /// <summary>The steps spent so far.</summary>
    public long Spent => m_spent;

    #endregion
}
