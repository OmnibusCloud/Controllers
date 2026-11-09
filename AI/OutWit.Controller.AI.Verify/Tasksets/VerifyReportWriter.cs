using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Assembles the verdict report Collect returns: aggregate metrics plus one verdict
/// record per task, ordered by task index. Verdict-first and kilobyte-scale — the small,
/// deterministic output the suitability filter wants and an RLVR pipeline consumes.
///
/// The report is the run's identity: it carries only the fields two honest executions
/// agree on wherever they ran (verdict, exit code, per-case outcome), so it is byte-identical
/// for a taskset regardless of placement. Fuel is reproducible on a host but drifts between
/// hosts for CPython tasks, peak memory can differ by a page with it, and wall time is
/// hardware; all three are metrics and live per task in the provenance document
/// (<see cref="VerifyProvenanceWriter"/>).
/// </summary>
public static class VerifyReportWriter
{
    /// <summary>Writes the verdict report: the aggregate summary and one identity record per task, ordered by index.</summary>
    /// <param name="results">The results of every execution, in any order; a task executed twice gets one record.</param>
    /// <returns>The UTF-8 JSON bytes.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultData> results)
    {
        var ordered = VerifyExecutions.Canonical(results);

        var histogram = Enum.GetValues<VerifyVerdict>()
            .ToDictionary(verdict => verdict.ToString(), verdict => ordered.Count(r => r.Verdict == verdict));

        var passCount = ordered.Count(r => r.Verdict == VerifyVerdict.Pass);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("summary");
            writer.WriteNumber("total", ordered.Count);
            writer.WriteNumber("pass", passCount);
            writer.WriteNumber("pass_rate", ordered.Count == 0 ? 0.0 : (double)passCount / ordered.Count);
            writer.WriteStartObject("verdicts");
            foreach (var (verdict, count) in histogram)
                writer.WriteNumber(verdict, count);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartArray("results");
            foreach (var result in ordered)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", result.TaskIndex);
                writer.WriteString("verdict", result.Verdict.ToString());
                writer.WriteNumber("exit", result.ExitCode);
                if (result.CaseResults.Count > 0)
                {
                    writer.WriteStartArray("cases");
                    foreach (var caseResult in result.CaseResults)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("case", caseResult.CaseIndex);
                        writer.WriteBoolean("passed", caseResult.Passed);
                        writer.WriteString("verdict", caseResult.Verdict.ToString());
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>Parses the report back — the shape a consumer (and the e2e test) reads.</summary>
    public static VerifyReportSummary ReadSummary(byte[] reportBytes)
    {
        using var document = JsonDocument.Parse(reportBytes);
        var summary = document.RootElement.GetProperty("summary");
        var total = summary.GetProperty("total").GetInt32();
        var pass = summary.GetProperty("pass").GetInt32();
        return new VerifyReportSummary(total, pass, Encoding.UTF8.GetString(reportBytes));
    }
}

public sealed record VerifyReportSummary(int Total, int Pass, string Json);
