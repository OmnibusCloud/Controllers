using System.Text.Encodings.Web;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the outputs of a run as a corpus: one JSON line per task, ordered by index, with the
/// verdict, exit code, stdout and stderr (and the truncation flags when set). Outputs are
/// deterministic, so the corpus is byte-identical for a taskset like the verdict report. A task
/// the nodes could not settle has a line too, without outputs: <c>NoResult</c> with its reason
/// code, or <c>Disputed</c>. The lines are written one at a time to a stream, so a corpus is as
/// large as its run and not as large as memory.
/// </summary>
public static class VerifyCorpusWriter
{
    #region Constants

    // Outputs verbatim: no HTML-safe escaping of +, <, > or non-ASCII (the corpus is read by parsers, never embedded in HTML).
    private static readonly JsonWriterOptions WRITER_OPTIONS = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const byte LINE_END = (byte)'\n';

    #endregion

    #region Functions

    /// <summary>
    /// Serializes results that each settle their task as UTF-8 JSON lines.
    /// </summary>
    /// <param name="results">One result per task, in any order.</param>
    /// <returns>The corpus bytes, one line per task, each terminated by a newline.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultData> results)
    {
        return Write(results.OrderBy(result => result.TaskIndex).Select(VerifySettledTask.Of));
    }

    /// <summary>
    /// Serializes settled tasks as UTF-8 JSON lines.
    /// </summary>
    /// <param name="tasks">The tasks, ordered by index.</param>
    /// <returns>The corpus bytes, one line per task, each terminated by a newline.</returns>
    public static byte[] Write(IEnumerable<VerifySettledTask> tasks)
    {
        using var stream = new MemoryStream();
        foreach (var task in tasks)
            WriteLine(stream, task);

        return stream.ToArray();
    }

    /// <summary>Writes one task's line.</summary>
    /// <param name="stream">The corpus.</param>
    /// <param name="task">The task.</param>
    public static void WriteLine(Stream stream, VerifySettledTask task)
    {
        using (var writer = new Utf8JsonWriter(stream, WRITER_OPTIONS))
        {
            var result = task.Result;

            writer.WriteStartObject();
            writer.WriteNumber("index", task.Index);
            writer.WriteString("verdict", task.Verdict.ToString());
            writer.WriteNumber("exit", task.ExitCode);

            if (task.Verdict == VerifyVerdict.NoResult)
                writer.WriteString("reason", task.Reason);

            writer.WriteString("stdout", result?.Stdout ?? "");
            if (result?.StdoutTruncated == true)
                writer.WriteBoolean("stdout_truncated", true);
            writer.WriteString("stderr", result?.Stderr ?? "");
            if (result?.StderrTruncated == true)
                writer.WriteBoolean("stderr_truncated", true);

            // A task with a suite ran once per case: each case's own outcome and output.
            if (result is { CaseResults.Count: > 0 })
            {
                writer.WriteStartArray("cases");
                foreach (var caseResult in result.CaseResults)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("case", caseResult.CaseIndex);
                    writer.WriteBoolean("passed", caseResult.Passed);
                    writer.WriteString("verdict", caseResult.Verdict.ToString());
                    writer.WriteNumber("exit", caseResult.ExitCode);
                    writer.WriteString("stdout", caseResult.ActualStdout);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        stream.WriteByte(LINE_END);
    }

    #endregion
}
