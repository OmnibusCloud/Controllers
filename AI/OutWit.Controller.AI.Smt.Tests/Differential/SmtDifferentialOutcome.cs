namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// One generated term, what the evaluator made of it and what the solver said.
/// </summary>
internal sealed class SmtDifferentialOutcome
{
    #region Functions

    /// <summary>The term and what became of it, on one line, for an assertion's message.</summary>
    /// <returns>The line.</returns>
    public override string ToString()
    {
        return $"{Term.Theory} #{Term.Number} ({Term.Sort}): {Term.Term} -> evaluator {Value ?? Reason ?? "nothing"}; {Detail}";
    }

    #endregion

    #region Properties

    /// <summary>The term.</summary>
    public required SmtGeneratedTerm Term { get; init; }

    /// <summary>What became of it.</summary>
    public required SmtDifferentialVerdict Verdict { get; init; }

    /// <summary>The evaluator's value, written as a term of the sort; null when it gave none.</summary>
    public string? Value { get; init; }

    /// <summary>Why the evaluator gave no value: its reason code, with the operator for an unsupported one; null when it gave one.</summary>
    public string? Reason { get; init; }

    /// <summary>What the solver said, or what went wrong.</summary>
    public string Detail { get; init; } = "";

    #endregion
}
