using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Turns the runs of a task's suite into its result: one case record per run, the task's
/// verdict from the first failing case, metrics summed (fuel, wall) or maxed (peak memory),
/// the last run's streams kept as the task's. The pass rule: a case passes when the program
/// ended by itself with the expected exit code and, if given, the exact expected stdout.
/// <para>
/// The cases of one task share its stdout cap: each run may print what the runs before it left,
/// so a task keeps no more output than its cap however many cases it has. A run the node could
/// not carry to an end of its own (<see cref="VerifyRunOutcome.Fault"/>) ends the suite: the task
/// has no verdict then.
/// </para>
/// </summary>
internal static class VerifySuiteEvaluator
{
    #region Functions

    /// <summary>
    /// Runs every case through <paramref name="run"/> and aggregates.
    /// </summary>
    /// <param name="task">The task (its suite, args and index).</param>
    /// <param name="stdoutBudget">The bytes of stdout the task's cases may keep together.</param>
    /// <param name="run">Executes one case: the case's arguments appended to the task's, the case's stdin, the stdout bytes it may keep (-1 for none).</param>
    /// <param name="cancellationToken">Checked between cases; a cancelled suite throws.</param>
    /// <returns>The task's result.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled between two cases.</exception>
    public static VerifyResultData Evaluate(VerifyTaskData task, int stdoutBudget, Func<IReadOnlyList<string>, string?, int, VerifyRunOutcome> run, CancellationToken cancellationToken = default)
    {
        var suite = task.Suite!;
        var caseResults = new List<VerifyCaseResultData>(suite.Cases.Count);

        long totalFuel = 0;
        long peakMemory = 0;
        var totalWall = 0;
        var allPassed = true;
        var firstFailure = VerifyVerdict.Pass;
        var firstFailureExit = 0;
        var left = Math.Max(0, stdoutBudget);
        VerifyRunOutcome? last = null;

        for (var i = 0; i < suite.Cases.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var testCase = suite.Cases[i];
            var outcome = run(task.Args.Concat(testCase.Args).ToList(), testCase.Stdin, left > 0 ? left : VerifyRunOutcome.NO_STDOUT);
            if (outcome.Fault != null)
                return VerifyWasmSandbox.Faulted(task.TaskIndex, outcome.Fault);

            last = outcome;
            left -= outcome.StdoutBytes;

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
    /// The verdict of one case. A case is met when the program ended by itself (it returned or
    /// called exit; a crash is not an exit, whatever number stands for it) with the expected exit
    /// code and, when one is given, the expected stdout: then it passes, whatever the program
    /// wrote to stderr. Otherwise a run that did not end by itself, or that ended for want of
    /// memory, is reported as what ended it (a crash, a budget, the memory or output cap). An
    /// exit with the expected code but the wrong output is a Fail, and so is one that was
    /// expected to signal with a non-zero code and did not; a non-zero exit where 0 was expected
    /// is a RuntimeError.
    /// </summary>
    /// <param name="outcome">The run.</param>
    /// <param name="testCase">The case.</param>
    /// <param name="passed">Whether the case passed.</param>
    /// <returns>The case's verdict.</returns>
    public static VerifyVerdict CaseVerdict(VerifyRunOutcome outcome, VerifySuiteCaseData testCase, out bool passed)
    {
        var exitMatches = outcome.ExitCode == testCase.ExpectedExitCode;
        passed = outcome.Exited
                 && exitMatches
                 && (testCase.ExpectedStdout == null || outcome.Stdout == testCase.ExpectedStdout);

        if (passed)
            return VerifyVerdict.Pass;

        // Not met. A run that did not end by itself, or that ended for want of memory, is
        // reported as what ended it.
        var completed = outcome.Exited && outcome.Verdict is VerifyVerdict.Pass or VerifyVerdict.RuntimeError;
        if (!completed)
            return outcome.Verdict is VerifyVerdict.Pass ? VerifyVerdict.RuntimeError : outcome.Verdict;

        if (exitMatches)
            return VerifyVerdict.Fail;

        return testCase.ExpectedExitCode == 0 ? VerifyVerdict.RuntimeError : VerifyVerdict.Fail;
    }

    #endregion
}
