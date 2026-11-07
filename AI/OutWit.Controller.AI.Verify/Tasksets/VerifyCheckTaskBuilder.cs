using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Turns generator results into check tasks: every candidate that passed with output becomes
/// one run of the checker with that output on stdin, under the candidate's task index — so the
/// check report lines up with the generators. Candidates without a usable output are skipped
/// with a note; the corpus still records them.
/// </summary>
public static class VerifyCheckTaskBuilder
{
    #region Functions

    /// <summary>
    /// Builds the check tasks for a set of candidates.
    /// </summary>
    /// <param name="candidates">Generator results, in any order.</param>
    /// <param name="checker">The checker spec.</param>
    /// <returns>The tasks (ordered by index) and one note per skipped candidate.</returns>
    public static VerifyCheckPlan Build(IReadOnlyList<VerifyResultData> candidates, VerifyCheckerSpec checker)
    {
        var tasks = new List<VerifyTaskData>();
        var skipped = new List<string>();

        foreach (var candidate in candidates.OrderBy(result => result.TaskIndex))
        {
            if (candidate.Verdict != VerifyVerdict.Pass)
            {
                skipped.Add($"candidate {candidate.TaskIndex}: {candidate.Verdict}");
                continue;
            }

            if (candidate.StdoutTruncated)
            {
                skipped.Add($"candidate {candidate.TaskIndex}: output truncated");
                continue;
            }

            if (candidate.Stdout.Length == 0)
            {
                skipped.Add($"candidate {candidate.TaskIndex}: empty output");
                continue;
            }

            tasks.Add(new VerifyTaskData
            {
                TaskIndex = candidate.TaskIndex,
                RuntimeId = checker.RuntimeId,
                EntryPoint = checker.EntryPoint,
                Sources = checker.Sources.Select(source => source.Clone()).ToList(),
                Args = checker.Args.ToList(),
                Stdin = candidate.Stdout,
                Limits = checker.Limits?.Clone(),
                Suite = new VerifySuiteData
                {
                    Cases =
                    [
                        new VerifySuiteCaseData
                        {
                            Stdin = candidate.Stdout,
                            ExpectedStdout = checker.ExpectedStdout,
                            ExpectedExitCode = checker.ExpectedExitCode
                        }
                    ]
                }
            });
        }

        return new VerifyCheckPlan(tasks, skipped);
    }

    #endregion
}

/// <summary>The check tasks and the candidates that produced none.</summary>
/// <param name="Tasks">Check tasks ordered by index.</param>
/// <param name="Skipped">One note per candidate without a check task.</param>
public sealed record VerifyCheckPlan(List<VerifyTaskData> Tasks, List<string> Skipped);
