namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A problem with a model that satisfies it, in the spelling a solver prints: what the
/// evaluator must validate, and what the mutation, fuzz and concurrency tests start from.
/// </summary>
internal sealed class SmtFixture
{
    #region Constructors

    /// <summary>
    /// Creates a fixture.
    /// </summary>
    /// <param name="name">A short name, unique among the fixtures.</param>
    /// <param name="logic">The logic the problem is of.</param>
    /// <param name="problem">The problem's text: declarations, definitions and assertions.</param>
    /// <param name="model">The answer of <c>(get-model)</c>, or null.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>, or null.</param>
    public SmtFixture(string name, string logic, string problem, string? model, string? values)
    {
        Name = name;
        Logic = logic;
        Problem = problem;
        Model = model;
        Values = values;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The fixture's name: what a test report shows.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString()
    {
        return Name;
    }

    #endregion

    #region Properties

    /// <summary>A short name, unique among the fixtures.</summary>
    public string Name { get; }

    /// <summary>The logic the problem is of.</summary>
    public string Logic { get; }

    /// <summary>The problem's text.</summary>
    public string Problem { get; }

    /// <summary>The answer of <c>(get-model)</c>, or null.</summary>
    public string? Model { get; }

    /// <summary>The answer of <c>(get-value (...))</c>, or null.</summary>
    public string? Values { get; }

    #endregion
}
