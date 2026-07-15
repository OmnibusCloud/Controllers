using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Assembles the verdict report Collect returns: aggregate metrics plus one verdict
/// record per task, ordered by task index. Verdict-first and kilobyte-scale — the small,
/// deterministic output the suitability filter wants and an RLVR pipeline consumes.
///
/// The report is byte-deterministic for a given taskset: it carries only reproducible
/// fields (verdict, exit, fuel, peak memory), never wall time — wall time is
/// hardware-dependent telemetry and lives on <see cref="VerifyResultData"/> for the
/// allocator, not in the reward artifact.
/// </summary>
public static class VerifyReportWriter
{
    public static byte[] Write(IReadOnlyList<VerifyResultData> results)
    {
        var ordered = results.OrderBy(result => result.TaskIndex).ToList();

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
            writer.WriteNumber("total_fuel", ordered.Sum(r => r.FuelConsumed));
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
                writer.WriteNumber("fuel", result.FuelConsumed);
                writer.WriteNumber("peak_memory", result.PeakMemoryBytes);
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
