using System.Text.Json;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the provenance document of a run: one entry per accepted execution of a batch (the
/// round it ran in, the platform, the engine, the controller, the runtime module and its hash,
/// the task indices it covered and each task's metrics — fuel, peak memory, wall time). It does
/// not say which machine ran what, and it says nothing a machine could be measured by: not how
/// many programs a node ran at once (a lab that sets the memory cap and the degree could read a
/// node's cores and memory off that number) and not how long a batch took as a whole. The
/// entries follow from their content, not from the order the batches came back in; the metrics
/// make the document a record, not an identity — the verdict report is the identity.
/// </summary>
internal static class VerifyProvenanceWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "verify-provenance/2";

    private const int FLUSH_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the accepted batches of a run as UTF-8 JSON.
    /// </summary>
    /// <param name="rounds">The rounds of the run, in the order they ran.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IEnumerable<VerifyRound> rounds)
    {
        using var stream = new MemoryStream();
        Write(stream, rounds);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the accepted batches of a run to a stream as UTF-8 JSON: the document has a record
    /// a task, so it goes out as it is made.
    /// </summary>
    /// <param name="stream">The document.</param>
    /// <param name="rounds">The rounds of the run, in the order they ran.</param>
    public static void Write(Stream stream, IEnumerable<VerifyRound> rounds)
    {
        var entries = rounds
            .SelectMany(round => round.Accepted.SelectMany(package => package.Batches.Select(batch => (Round: round.Name, Batch: batch))))
            .Where(entry => entry.Batch.Executions.Count > 0)
            .Select(entry => (entry.Round, entry.Batch, First: entry.Batch.Executions.Min(execution => execution.Index)))
            .OrderBy(entry => entry.First)
            .ThenBy(entry => entry.Round, StringComparer.Ordinal)
            .ThenBy(entry => entry.Batch.Diagnostics.RuntimeTarget ?? "", StringComparer.Ordinal)
            .ThenBy(entry => entry.Batch.Executions.Sum(execution => execution.Fuel))
            .ThenBy(entry => VerifySums.WallMs(entry.Batch.Executions))
            .ToList();

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);

        writer.WriteStartArray("batches");
        foreach (var (round, batch, _) in entries)
        {
            WriteBatch(writer, round, batch);

            if (writer.BytesPending >= FLUSH_BYTES)
                writer.Flush();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteBatch(Utf8JsonWriter writer, string round, VerifyAcceptedBatch batch)
    {
        var diagnostics = batch.Diagnostics;
        var executions = batch.Executions.OrderBy(execution => execution.Index).ToList();

        writer.WriteStartObject();
        writer.WriteString("round", round);
        writer.WriteString("runtime_target", diagnostics.RuntimeTarget);
        writer.WriteString("wasmtime", diagnostics.WasmtimeVersion);
        writer.WriteString("controller", diagnostics.ControllerVersion);
        writer.WriteString("runtime", diagnostics.RuntimeId);
        writer.WriteString("runtime_sha256", diagnostics.RuntimeSha256);
        // A sum of numbers nodes reported: it stops at the largest value and never throws.
        writer.WriteNumber("fuel", executions.Sum(execution => execution.Fuel));

        writer.WriteStartArray("tasks");
        foreach (var execution in executions)
            writer.WriteNumberValue(execution.Index);
        writer.WriteEndArray();

        // The per-task metrics: what a run cost, never what it computed (that is the report's).
        writer.WriteStartArray("metrics");
        foreach (var execution in executions)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", execution.Index);
            writer.WriteNumber("fuel", execution.Fuel);
            writer.WriteNumber("peak_memory", execution.PeakMemory);
            writer.WriteNumber("wall_ms", execution.WallMs);
            if (!execution.HasVerdict)
                writer.WriteString("fault", execution.Fault);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    #endregion
}
