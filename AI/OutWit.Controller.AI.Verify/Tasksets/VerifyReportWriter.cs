using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Assembles the verdict report: aggregate metrics plus one verdict record per task, ordered by
/// task index. Verdict-first and kilobyte-scale — the small, deterministic output the
/// suitability filter wants and an RLVR pipeline consumes.
///
/// The report is the run's identity: it carries only the fields two honest executions
/// agree on wherever they ran (verdict, exit code, per-case outcome), so it is byte-identical
/// for a taskset regardless of placement, as long as every node did its work and told the truth.
/// A task the nodes could not settle is in it as such: <c>NoResult</c> with a reason code, or
/// <c>Disputed</c>, never left out and never counted as a pass. The summary counts the result
/// packages the server refused and the disputes it had to settle; all three are zero in a run
/// without lying or failing nodes. Fuel, peak memory and wall time are metrics and live per task
/// in the provenance document (<see cref="VerifyProvenanceWriter"/>).
/// </summary>
public static class VerifyReportWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "verify-report/2";

    #endregion

    #region Functions

    /// <summary>Writes the report of tasks that were each settled by one result: no refusals, no disputes.</summary>
    /// <param name="results">One result per task, in any order.</param>
    /// <returns>The UTF-8 JSON bytes.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultData> results)
    {
        var tasks = results.OrderBy(result => result.TaskIndex).Select(VerifySettledTask.Of).ToList();
        return Write(VerifySettlementSummary.Of(tasks), tasks);
    }

    /// <summary>Writes the report of a settled run.</summary>
    /// <param name="summary">The run's counts.</param>
    /// <param name="tasks">The tasks, ordered by index.</param>
    /// <returns>The UTF-8 JSON bytes.</returns>
    public static byte[] Write(VerifySettlementSummary summary, IEnumerable<VerifySettledTask> tasks)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteHead(writer, summary);
            foreach (var task in tasks)
                WriteTask(writer, task);
            WriteTail(writer);
        }

        return stream.ToArray();
    }

    /// <summary>Opens the document: the schema tag, the summary, and the array of results.</summary>
    /// <param name="writer">The document's writer.</param>
    /// <param name="summary">The run's counts.</param>
    public static void WriteHead(Utf8JsonWriter writer, VerifySettlementSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);

        writer.WriteStartObject("summary");
        writer.WriteNumber("total", summary.Total);
        writer.WriteNumber("pass", summary.Pass);
        writer.WriteNumber("pass_rate", summary.Total == 0 ? 0.0 : (double)summary.Pass / summary.Total);
        writer.WriteStartObject("verdicts");
        foreach (var verdict in Enum.GetValues<VerifyVerdict>())
            writer.WriteNumber(verdict.ToString(), summary.Verdicts.GetValueOrDefault(verdict));
        writer.WriteEndObject();
        writer.WriteNumber("rejected_results", summary.RejectedResults);
        writer.WriteNumber("disputes", summary.Disputes);
        writer.WriteNumber("disputes_resolved", summary.DisputesResolved);
        writer.WriteEndObject();

        writer.WriteStartArray("results");
    }

    /// <summary>Writes one task's record.</summary>
    /// <param name="writer">The document's writer, inside the array of results.</param>
    /// <param name="task">The task.</param>
    public static void WriteTask(Utf8JsonWriter writer, VerifySettledTask task)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", task.Index);
        writer.WriteString("verdict", task.Verdict.ToString());
        writer.WriteNumber("exit", task.ExitCode);

        if (task.Verdict == VerifyVerdict.NoResult)
            writer.WriteString("reason", task.Reason);

        if (task.Result is { CaseResults.Count: > 0 } result)
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

    /// <summary>Closes the array of results and the document.</summary>
    /// <param name="writer">The document's writer.</param>
    public static void WriteTail(Utf8JsonWriter writer)
    {
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Parses the report back — the shape a consumer (and the e2e test) reads.</summary>
    /// <param name="reportBytes">The report.</param>
    /// <returns>Its totals and its text.</returns>
    public static VerifyReportSummary ReadSummary(byte[] reportBytes)
    {
        using var document = JsonDocument.Parse(reportBytes);
        var summary = document.RootElement.GetProperty("summary");
        var total = summary.GetProperty("total").GetInt32();
        var pass = summary.GetProperty("pass").GetInt32();
        return new VerifyReportSummary(total, pass, Encoding.UTF8.GetString(reportBytes));
    }

    #endregion
}

/// <summary>A report's totals and its text.</summary>
/// <param name="Total">The tasks in the report.</param>
/// <param name="Pass">The tasks with verdict Pass.</param>
/// <param name="Json">The report as text.</param>
public sealed record VerifyReportSummary(int Total, int Pass, string Json);
