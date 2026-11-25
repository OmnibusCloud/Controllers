using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>What one package came to on a budget of sessions (<see cref="SmtBudgetRig"/>).</summary>
internal sealed class SmtBudgetRun
{
    #region Properties

    /// <summary>The package's rows, in the order they were written; empty when it returned none.</summary>
    public List<SmtResultData> Rows { get; init; } = [];

    /// <summary>The sessions of its size it was given at once; 0 when it was not let in or refused.</summary>
    public int Degree { get; init; }

    /// <summary>What decided the degree.</summary>
    public string Reason { get; init; } = "";

    /// <summary>The code it was refused with; null when it was not.</summary>
    public string? Fault { get; init; }

    /// <summary>Whether it was cancelled: it returned nothing.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Whether a cancelled package left its file of results behind.</summary>
    public bool ResultsLeft { get; init; }

    #endregion
}
