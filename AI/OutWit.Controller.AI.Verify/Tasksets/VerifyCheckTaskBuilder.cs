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
    /// Builds the check tasks for a set of candidates.
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
            if (candidate.Verdict != VerifyVerdict.Pass)
            {
                skipped.Add($"candidate {candidate.Index}: {candidate.Verdict}");
                continue;
            }

            if (candidate.StdoutTruncated)
            {
                skipped.Add($"candidate {candidate.Index}: output truncated");
                continue;
            }

            if (candidate.Stdout.Length == 0)
            {
                skipped.Add($"candidate {candidate.Index}: empty output");
                continue;
            }

            tasks.Add(new VerifyTaskData
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
            });
        }

        return new VerifyCheckPlan(tasks, skipped);
    }

    /// <summary>
    /// Reads the candidates of a generator run from its ledger: the outputs, one settled task a line.
    /// </summary>
    /// <param name="ledgerPath">The ledger's local file.</param>
    /// <returns>The candidates, in the ledger's order (by index).</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static List<VerifyCandidate> ReadCandidates(string ledgerPath)
    {
        var candidates = new List<VerifyCandidate>();

        using var outputs = VerifyLedger.Open(ledgerPath, VerifyLedger.OUTPUTS, out var length);
        var end = outputs.Position + length;
        using var reader = new StreamReader(outputs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);

        // The outputs are the ledger's last document: its lines run to the end of the file.
        while (outputs.Position <= end && reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            candidates.Add(new VerifyCandidate(
                root.GetProperty("index").GetInt32(),
                Enum.Parse<VerifyVerdict>(root.GetProperty("verdict").GetString() ?? ""),
                root.GetProperty("stdout").GetString() ?? "",
                root.TryGetProperty("stdout_truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True));
        }

        return candidates;
    }

    #endregion
}

/// <summary>The check tasks and the candidates that produced none.</summary>
/// <param name="Tasks">Check tasks ordered by index.</param>
/// <param name="Skipped">One note per candidate without a check task.</param>
public sealed record VerifyCheckPlan(List<VerifyTaskData> Tasks, List<string> Skipped);
