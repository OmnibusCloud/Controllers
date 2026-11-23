namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// One generated closed term: the declarations it needs, the model that interprets what they
/// declare, the term and its sort.
/// </summary>
internal sealed class SmtGeneratedTerm
{
    #region Properties

    /// <summary>The theory the term was generated for.</summary>
    public required SmtDifferentialTheory Theory { get; init; }

    /// <summary>The term's number in its theory, from 0.</summary>
    public required int Number { get; init; }

    /// <summary>The commands the term needs: the logic, datatypes, sorts, declarations.</summary>
    public required string Prelude { get; init; }

    /// <summary>The answer of <c>get-model</c> that interprets what the prelude declares; null when it declares nothing to interpret.</summary>
    public string? Model { get; init; }

    /// <summary>The elements of the model's universes, which the solver is told are distinct; none when there are none.</summary>
    public IReadOnlyList<string> Elements { get; init; } = [];

    /// <summary>The term.</summary>
    public required string Term { get; init; }

    /// <summary>The term's sort.</summary>
    public required SmtGenSort Sort { get; init; }

    #endregion
}
