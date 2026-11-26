using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// One enumeration of <see cref="SmtProjectionCases"/>: a small problem, the constants it is
/// projected on, and what becomes of it.
/// </summary>
/// <param name="Id">The case's name, once in the list.</param>
/// <param name="Text">The problem's text.</param>
/// <param name="MaxModels">The most models to look for.</param>
/// <param name="Project">The constants the models are told apart by.</param>
/// <param name="Words">
/// What the real solver's outcome says in a few words (<see cref="SmtSolved.Words"/>) for an
/// enumeration whose values the solver reads back; empty for one that ends at a value it does
/// not (<see cref="Declared"/>), which is held to what such an end is, not to a number of models.
/// </param>
internal sealed record SmtProjectionCase(string Id, string Text, int MaxModels, IReadOnlyList<string> Project, string Words)
{
    #region Functions

    /// <summary>The case as a row of a problem set: what a lab would write.</summary>
    /// <returns>One JSON line.</returns>
    public string Row()
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = Id,
            ["query"] = "enumerate",
            ["smt2"] = Text,
            ["project"] = Project,
            ["max_models"] = MaxModels
        });
    }

    /// <summary>The case as a problem that travelled to a node, whatever a host would have said of it.</summary>
    /// <returns>The problem.</returns>
    public SmtProblemData Problem()
    {
        return SmtSolverProblems.Enumerate(Id, Text, MaxModels, [.. Project]);
    }

    /// <summary>The case's name, as a test's log shows it.</summary>
    /// <returns>The id.</returns>
    public override string ToString()
    {
        return Id;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The sort the problem only declares (<c>declare-sort</c>) whose elements stand in the values
    /// of a projected constant. The solver names such an element by a name of its own and does not
    /// take it back, so the enumeration ends at the first model that holds one, with the models it
    /// found. Null for an enumeration whose values the solver reads back.
    /// </summary>
    public string? Declared { get; init; }

    /// <summary>Whether the enumeration ends at a model whose values the solver does not take back.</summary>
    public bool EndsAtAValueNotReadBack => Declared != null;

    #endregion
}
