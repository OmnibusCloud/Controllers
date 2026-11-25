using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The host's evaluator asked about one row, without a round around it: the problem as a set's
/// reader takes its row, a row made up as a node writes one, and the evaluator's word in the
/// words a document writes. For the rows the intake of a round would not let through, which the
/// evaluator must still stand: it is the second line behind the intake.
/// <para>
/// The controller's types stand in method bodies only, as in every rig: a signature here names
/// the model package's types and texts.
/// </para>
/// </summary>
internal static class SmtHostChecks
{
    #region Functions

    /// <summary>The problem of the one row of a set, as a run reads it.</summary>
    /// <param name="line">The row: one JSON line of a problem set.</param>
    /// <returns>The problem as it travels.</returns>
    public static SmtProblemData Problem(string line)
    {
        return SmtSettleRig.Read(SmtSets.Of([line]), new SmtOptionsData()).Single();
    }

    /// <summary>What the host's evaluator says of a row of a problem.</summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="row">The row, as a node gave it.</param>
    /// <returns>The class, the reason and the detail, as a row of the results writes them.</returns>
    public static (string Class, string Reason, string Detail) Check(SmtProblemData problem, SmtResultData row)
    {
        var evaluator = SmtHostEvaluator.Of(problem) ?? throw new InvalidOperationException("the host's sanitizer does not take the problem");
        var check = evaluator.Check(row);
        return (check.Class, check.Reason, check.Detail);
    }

    /// <summary>
    /// The terms a node's session asks the values of for a problem, as the session writes them
    /// into its command: what the solver's answer echoes.
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <returns>The terms as text, in order; null for a problem a node refuses.</returns>
    public static List<string>? AskedByASession(SmtProblemData problem)
    {
        var plan = SmtSolvePlan.Prepare(problem, SmtProfile.Envelope(new SmtOptionsData()), out _);
        if (plan == null)
            return null;

        return (problem.Query == SmtQuery.Enumerate ? plan.Projection : plan.Values).Select(term => term.ToString()).ToList();
    }

    /// <summary>The terms the host's evaluator holds a row's values to for a problem.</summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <returns>The terms as text, in order; null when the host cannot make them.</returns>
    public static List<string>? AskedByTheHost(SmtProblemData problem)
    {
        var sanitized = SmtSanitizer.Sanitize(problem.Text);
        return sanitized.Problem == null ? null : SmtAskedTerms.Of(sanitized.Problem, problem)?.Select(term => term.ToString()).ToList();
    }

    #endregion
}
