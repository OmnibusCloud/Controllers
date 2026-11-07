using System.Text.Encodings.Web;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the outputs of a run as a corpus: one JSON line per task, ordered by index, with the
/// verdict, exit code, stdout and stderr (and the truncation flags when set). Outputs are
/// deterministic, so the corpus is byte-identical for a taskset like the verdict report.
/// </summary>
public static class VerifyCorpusWriter
{
    #region Constants

    // Outputs verbatim: no HTML-safe escaping of +, <, > or non-ASCII (the corpus is read by parsers, never embedded in HTML).
    private static readonly JsonWriterOptions WRITER_OPTIONS = new() { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the results as UTF-8 JSON lines.
    /// </summary>
    /// <param name="results">Task results, in any order.</param>
    /// <returns>The corpus bytes, one line per task, each terminated by a newline.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultData> results)
    {
        using var stream = new MemoryStream();
        foreach (var result in results.OrderBy(result => result.TaskIndex))
        {
            using (var writer = new Utf8JsonWriter(stream, WRITER_OPTIONS))
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", result.TaskIndex);
                writer.WriteString("verdict", result.Verdict.ToString());
                writer.WriteNumber("exit", result.ExitCode);
                writer.WriteString("stdout", result.Stdout);
                if (result.StdoutTruncated)
                    writer.WriteBoolean("stdout_truncated", true);
                writer.WriteString("stderr", result.Stderr);
                if (result.StderrTruncated)
                    writer.WriteBoolean("stderr_truncated", true);
                writer.WriteEndObject();
            }

            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }

    #endregion
}
