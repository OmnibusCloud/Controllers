using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// One Grid.ForEach call returns one batch per execution, in no particular order, and a sampled
/// batch has two. This picks the result that stands for a task, by its identity fields rather
/// than by arrival, so the same executions always give the same report.
/// </summary>
public static class VerifyExecutions
{
    #region Functions

    /// <summary>The result standing for each task, in task order.</summary>
    /// <param name="executions">The results of all executions, any order.</param>
    /// <returns>One result per task index.</returns>
    public static List<VerifyResultData> Canonical(IEnumerable<VerifyResultData> executions)
    {
        return executions
            .GroupBy(execution => execution.TaskIndex)
            .OrderBy(group => group.Key)
            .Select(group => Pick(group))
            .ToList();
    }

    /// <summary>The result standing for one task: ordered by verdict, exit code, then outputs.</summary>
    /// <param name="executions">The results of one task; at least one.</param>
    /// <returns>The chosen result.</returns>
    public static VerifyResultData Pick(IEnumerable<VerifyResultData> executions)
    {
        return InRuleOrder(executions).First();
    }

    /// <summary>The results of one task in the order the rule ranks them.</summary>
    /// <param name="executions">The results of one task.</param>
    /// <returns>The ranked results.</returns>
    public static IEnumerable<VerifyResultData> InRuleOrder(IEnumerable<VerifyResultData> executions)
    {
        return executions
            .OrderBy(execution => execution.Verdict)
            .ThenBy(execution => execution.ExitCode)
            .ThenBy(execution => execution.Stdout, StringComparer.Ordinal)
            .ThenBy(execution => execution.Stderr, StringComparer.Ordinal);
    }

    #endregion
}
