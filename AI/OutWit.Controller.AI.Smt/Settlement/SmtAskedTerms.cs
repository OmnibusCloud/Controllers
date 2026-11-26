using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The terms a row's values answer, as the host knows them: they travel with the problem, from
/// the host's own reading of the set, and no node's word is taken for them. A check asks for the
/// values of the terms its row names, each sanitized here as a node's session sanitizes it; an
/// enumeration asks for the values of the constants it projects on, each as the problem declares
/// it; a proof asks for none. These are the terms a session writes into its <c>get-value</c>
/// (<c>SmtSolvePlan</c> makes them the same way on a node), and the solver echoes each of them
/// in its answer as it was written.
/// </summary>
internal static class SmtAskedTerms
{
    #region Functions

    /// <summary>
    /// The terms whose values a session of a problem asks for.
    /// </summary>
    /// <param name="problem">The problem as the host's sanitizer took it.</param>
    /// <param name="data">The problem as it travelled: its query and the terms beside it.</param>
    /// <returns>The terms, in the order they are asked; none for a query that asks for no value; null when a term is not one a session would ask (no node answers such a problem with values).</returns>
    public static IReadOnlyList<SmtExpression>? Of(SmtProblem problem, SmtProblemData data)
    {
        return data.Query switch
        {
            SmtQuery.Check => Terms(data.Values ?? []),
            SmtQuery.Enumerate => Constants(problem, data.Project ?? []),
            _ => []
        };
    }

    #endregion

    #region Tools

    private static IReadOnlyList<SmtExpression>? Terms(IReadOnlyList<string> texts)
    {
        var terms = new List<SmtExpression>(texts.Count);
        foreach (var text in texts)
        {
            var term = SmtSanitizer.SanitizeTerm(text ?? "");
            if (!term.IsAccepted)
                return null;

            terms.Add(term.Term);
        }

        return terms;
    }

    /// <summary>
    /// The constants of a projection, each written as the problem's declaration writes its name:
    /// by the one reading of a projection the reader of a set and the node share
    /// (<see cref="SmtProjection"/>), so that the host takes the projections they take and no other.
    /// </summary>
    private static IReadOnlyList<SmtExpression>? Constants(SmtProblem problem, IReadOnlyList<string> texts)
    {
        return SmtProjection.Take(problem, texts, out var constants) == null ? [.. constants.Select(constant => constant.Symbol)] : null;
    }

    #endregion
}
