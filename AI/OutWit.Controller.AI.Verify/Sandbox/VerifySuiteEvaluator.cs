using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Turns the runs of a task's suite into its result: one case record per run, the task's
/// verdict from the first failing case, metrics summed (fuel, wall) or maxed (peak memory),
/// the last run's streams kept as the task's. The pass rule: a case passes when the run
/// completed with the expected exit code and, if given, the exact expected stdout.
/// </summary>
internal static class VerifySuiteEvaluator
{
    #region Functions

    /// <summary>
    /// Runs every case through <paramref name="run"/> and aggregates.
    /// </summary>
    /// <param name="task">The task (its suite, args and index).</param>
    /// <param name="run">Executes one case: the case's arguments appended to the task's, the case's stdin.</param>
    /// <param name="cancellationToken">Checked between cases; a cancelled suite throws.</param>
    /// <returns>The task's result.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled between two cases.</exception>
    public static VerifyResultData Evaluate(VerifyTaskData task, Func<IReadOnlyList<string>, string?, VerifyRunOutcome> run, CancellationToken cancellationToken = default)
    {
        var suite = task.Suite!;
        var caseResults = new List<VerifyCaseResultData>(suite.Cases.Count);

        long totalFuel = 0;
        long peakMemory = 0;
        var totalWall = 0;
        var allPassed = true;
        var firstFailure = VerifyVerdict.Pass;
        var firstFailureExit = 0;
        VerifyRunOutcome? last = null;

        for (var i = 0; i < suite.Cases.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var testCase = suite.Cases[i];
            var outcome = run(task.Args.Concat(testCase.Args).ToList(), testCase.Stdin);
            last = outcome;

            totalFuel += outcome.FuelConsumed;
            peakMemory = Math.Max(peakMemory, outcome.PeakMemoryBytes);
            totalWall += outcome.WallMs;

            var caseVerdict = CaseVerdict(outcome, testCase, out var passed);
            caseResults.Add(new VerifyCaseResultData
            {
                CaseIndex = i,
                Passed = passed,
                Verdict = caseVerdict,
                ExitCode = outcome.ExitCode,
                ActualStdout = outcome.Stdout
            });

            if (!passed && allPassed)
            {
                allPassed = false;
                firstFailure = caseVerdict;
                firstFailureExit = outcome.ExitCode;
            }
        }

        return new VerifyResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = allPassed ? VerifyVerdict.Pass : firstFailure,
            ExitCode = allPassed ? 0 : firstFailureExit,
            Stdout = last?.Stdout ?? "",
            StdoutTruncated = last?.StdoutTruncated ?? false,
            Stderr = last?.Stderr ?? "",
            StderrTruncated = last?.StderrTruncated ?? false,
            CaseResults = caseResults,
            FuelConsumed = totalFuel,
            PeakMemoryBytes = peakMemory,
            WallMs = totalWall
        };
    }

    /// <summary>
    /// The verdict of one case. A run that reached its exit (code 0 or not) completed; a
    /// resource verdict (Timeout, Memory, Output) did not and is reported as-is. A completed
    /// run with the expected exit code but the wrong output is a Fail, and so is a run that
    /// was expected to signal with a non-zero code and did not. A crash the case did not
    /// expect (non-zero exit where 0 was expected) stays a RuntimeError.
    /// </summary>
    /// <param name="outcome">The run.</param>
    /// <param name="testCase">The case.</param>
    /// <param name="passed">Whether the case passed.</param>
    /// <returns>The case's verdict.</returns>
    public static VerifyVerdict CaseVerdict(VerifyRunOutcome outcome, VerifySuiteCaseData testCase, out bool passed)
    {
        var completed = outcome.Verdict is VerifyVerdict.Pass or VerifyVerdict.RuntimeError;
        var exitMatches = outcome.ExitCode == testCase.ExpectedExitCode;
        passed = completed
                 && exitMatches
                 && (testCase.ExpectedStdout == null || outcome.Stdout == testCase.ExpectedStdout);

        return passed ? VerifyVerdict.Pass
            : !completed ? outcome.Verdict
            : exitMatches ? VerifyVerdict.Fail
            : testCase.ExpectedExitCode == 0 ? VerifyVerdict.RuntimeError
            : VerifyVerdict.Fail;
    }

    #endregion
}

/// <summary>One guest invocation's outcome, before suite evaluation.</summary>
/// <param name="Verdict">The run's verdict.</param>
/// <param name="ExitCode">The exit code; -1 for a trap or a module that never ran.</param>
/// <param name="Stdout">Captured stdout, capped.</param>
/// <param name="StdoutTruncated">Whether stdout hit its cap.</param>
/// <param name="Stderr">Captured stderr, capped.</param>
/// <param name="StderrTruncated">Whether stderr hit its cap.</param>
/// <param name="FuelConsumed">Fuel consumed; the budget for a trap.</param>
/// <param name="PeakMemoryBytes">Peak linear memory.</param>
/// <param name="WallMs">Wall time of the invocation.</param>
internal sealed record VerifyRunOutcome(
    VerifyVerdict Verdict, int ExitCode,
    string Stdout, bool StdoutTruncated, string Stderr, bool StderrTruncated,
    long FuelConsumed, long PeakMemoryBytes, int WallMs);
