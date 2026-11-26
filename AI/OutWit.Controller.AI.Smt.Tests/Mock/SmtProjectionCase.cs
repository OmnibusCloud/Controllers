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
/// What the real solver's outcome says in a few words (<see cref="SmtSolved.Words"/>) when the
/// projection is taken; ignored when it is refused.
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
    /// The uninterpreted sort whose elements the values of a projected constant hold: the
    /// projection is refused, and the refusal names this sort. Null for a projection that is taken.
    /// </summary>
    public string? Uninterpreted { get; init; }

    /// <summary>The place of the entry of <see cref="Project"/> the refusal names, from 0.</summary>
    public int Entry { get; init; }

    /// <summary>Whether the projection is refused.</summary>
    public bool IsRefused => Uninterpreted != null;

    #endregion
}
