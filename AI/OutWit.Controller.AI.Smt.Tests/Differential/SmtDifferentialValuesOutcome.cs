using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// One generated term whose value the solver was asked for, and what the validator made of the
/// solver's answer: of the answer as it came, and of the same answer with its first value
/// changed into another.
/// </summary>
internal sealed class SmtDifferentialValuesOutcome
{
    #region Functions

    /// <summary>The term and what became of its value, on one line, for an assertion's message.</summary>
    /// <returns>The line.</returns>
    public override string ToString()
    {
        return $"{Term.Theory} #{Term.Number} ({Term.Sort}): {Term.Term} -> {(Answered ? $"{Class} {Reason} {Detail}".TrimEnd() : "no answer")}; {Note}";
    }

    #endregion

    #region Properties

    /// <summary>The term.</summary>
    public required SmtGeneratedTerm Term { get; init; }

    /// <summary>Whether the solver said <c>sat</c> and gave its model and its values.</summary>
    public bool Answered { get; init; }

    /// <summary>Whether the answer holds as many pairs as terms were asked, each pair's term reading as the term asked at its place.</summary>
    public bool Echoed { get; init; }

    /// <summary>The class of the solver's model without its values: what the answer was before its values were held to it.</summary>
    public SmtValidationClass Alone { get; init; }

    /// <summary>The reason of that class, or null.</summary>
    public string? AloneReason { get; init; }

    /// <summary>The class of the answer: the model, and the values held to the terms asked and to the model.</summary>
    public SmtValidationClass Class { get; init; }

    /// <summary>The reason of that class, or null.</summary>
    public string? Reason { get; init; }

    /// <summary>The detail of that class, or null.</summary>
    public string? Detail { get; init; }

    /// <summary>Whether the first value of the answer could be changed into another value of its sort.</summary>
    public bool Changed { get; init; }

    /// <summary>The class of the answer with that value changed.</summary>
    public SmtValidationClass ChangedClass { get; init; }

    /// <summary>The reason of that class, or null.</summary>
    public string? ChangedReason { get; init; }

    /// <summary>What the solver said of the term, or what went wrong.</summary>
    public string Note { get; init; } = "";

    #endregion
}
