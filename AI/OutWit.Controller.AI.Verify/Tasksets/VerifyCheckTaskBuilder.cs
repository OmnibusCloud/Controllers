using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Turns the settled results of a generator run into check tasks: every candidate that passed
/// with output becomes one run of the checker with that output on stdin, under the candidate's
/// task index — so the check report lines up with the generators. Candidates without a usable
/// output are skipped with a note; the corpus still records them. The candidates are read from
/// the generator run's ledger, where each task has one settled result: what is checked is what
/// the corpus holds, and nothing a single node added.
/// </summary>
public static class VerifyCheckTaskBuilder
{
    #region Functions

    /// <summary>
    /// Builds the check tasks for a set of candidates that are in memory.
    /// </summary>
    /// <param name="candidates">The settled generator results, one a task.</param>
    /// <param name="checker">The checker spec.</param>
    /// <returns>The tasks (ordered by index) and one note per skipped candidate.</returns>
    public static VerifyCheckPlan Build(IEnumerable<VerifyCandidate> candidates, VerifyCheckerSpec checker)
    {
        var tasks = new List<VerifyTaskData>();
        var skipped = new List<string>();

        foreach (var candidate in candidates.OrderBy(result => result.Index))
        {
            var task = CheckTask(candidate, checker, out var why);
            if (task == null)
                skipped.Add($"candidate {candidate.Index}: {why}");
            else
                tasks.Add(task);
        }

        return new VerifyCheckPlan(tasks, skipped);
    }

    /// <summary>
    /// The check task of one candidate: the checker run with the candidate's output on stdin,
    /// under the candidate's index.
    /// </summary>
    /// <param name="candidate">The settled generator result.</param>
    /// <param name="checker">The checker spec.</param>
    /// <param name="skipped">Why the candidate has no check, when it has none.</param>
    /// <returns>The task, or null for a candidate without a usable output.</returns>
    public static VerifyTaskData? CheckTask(VerifyCandidate candidate, VerifyCheckerSpec checker, out string? skipped)
    {
        skipped = Unusable(candidate);
        if (skipped != null)
            return null;

        return new VerifyTaskData
        {
            TaskIndex = candidate.Index,
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
        };
    }

    /// <summary>Why a candidate has no check: it did not pass, its output was cut, or it printed nothing.</summary>
    /// <param name="candidate">The settled generator result.</param>
    /// <returns>The reason, or null for a candidate that is checked.</returns>
    public static string? Unusable(VerifyCandidate candidate)
    {
        if (candidate.Verdict != VerifyVerdict.Pass)
            return candidate.Verdict.ToString();

        if (candidate.StdoutTruncated)
            return "output truncated";

        return candidate.Stdout.Length == 0 ? "empty output" : null;
    }

    /// <summary>
    /// Reads the candidates of a generator run from its ledger: the outputs, one settled task a
    /// line, in the ledger's order (by index). One candidate is held at a time: the outputs of a
    /// run are as large as the run.
    /// </summary>
    /// <param name="ledgerPath">The ledger's local file.</param>
    /// <returns>The candidates, read as they are asked for.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static IEnumerable<VerifyCandidate> ReadCandidates(string ledgerPath)
    {
        using var outputs = VerifyLedger.Open(ledgerPath, VerifyLedger.OUTPUTS, out _);
        using var reader = new StreamReader(outputs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);

        // The outputs are the ledger's last document: its lines run to the end of the file.
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            yield return new VerifyCandidate(
                root.GetProperty("index").GetInt32(),
                Enum.Parse<VerifyVerdict>(root.GetProperty("verdict").GetString() ?? ""),
                root.GetProperty("stdout").GetString() ?? "",
                root.TryGetProperty("stdout_truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True);
        }
    }

    #endregion
}
