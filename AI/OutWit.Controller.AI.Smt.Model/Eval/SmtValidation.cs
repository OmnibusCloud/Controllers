using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What checking one model against one problem gave: the class, why, and the counts behind it.
/// Nothing in it is text of the problem or of the model.
/// </summary>
public sealed class SmtValidation : ModelBase
{
    #region Model Base

    /// <summary>
    /// Whether another model is the same outcome.
    /// </summary>
    /// <param name="modelBase">The other model.</param>
    /// <param name="tolerance">Not used: every number is whole.</param>
    /// <returns>True for the same class, reason, detail and counts.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtValidation other)
            return false;

        return Class.Is(other.Class)
               && Reason.Is(other.Reason)
               && Detail.Is(other.Detail)
               && Assertions.Is(other.Assertions)
               && True.Is(other.True)
               && Defined.Is(other.Defined)
               && Steps.Is(other.Steps);
    }

    /// <summary>
    /// Copies the outcome.
    /// </summary>
    /// <returns>The same outcome.</returns>
    public override SmtValidation Clone()
    {
        return new SmtValidation
        {
            Class = Class,
            Reason = Reason,
            Detail = Detail,
            Assertions = Assertions,
            True = True,
            Defined = Defined,
            Steps = Steps
        };
    }

    #endregion

    #region Properties

    /// <summary>Validated, partial or failed.</summary>
    [ToString]
    public SmtValidationClass Class { get; init; }

    /// <summary>
    /// A code of <see cref="SmtValidationReasons"/>: why the class is partial or failed (the
    /// first reason met, in the order of the assertions); null for validated.
    /// </summary>
    [ToString]
    public string? Reason { get; init; }

    /// <summary>
    /// For <see cref="SmtValidationReasons.ASSERTION_FALSE"/> the 1-based index of the first
    /// false assertion; for <see cref="SmtValidationReasons.UNSUPPORTED_OPERATOR"/> the
    /// operator's name; otherwise null.
    /// </summary>
    [ToString]
    public string? Detail { get; init; }

    /// <summary>How many assertions the problem has.</summary>
    [ToString]
    public int Assertions { get; init; }

    /// <summary>How many of them evaluated to true.</summary>
    [ToString]
    public int True { get; init; }

    /// <summary>How many of the symbols the problem declares the model defines.</summary>
    [ToString]
    public int Defined { get; init; }

    /// <summary>How many steps of the budget the evaluation used.</summary>
    [ToString]
    public long Steps { get; init; }

    #endregion
}
