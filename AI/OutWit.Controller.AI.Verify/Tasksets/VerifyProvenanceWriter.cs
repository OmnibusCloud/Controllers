using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the provenance document of a run: one entry per execution of a batch (the platform,
/// wasmtime, controller, runtime module and hash, degree, wall time, the task indices it covered
/// and each task's metrics — fuel, peak memory, wall time). It does not say which machine ran
/// what. Batches are ordered by their first task index and the entries do not depend on the
/// order the batches came back in; the metrics make it a record, not an identity — the verdict
/// report is the identity.
/// </summary>
public static class VerifyProvenanceWriter
{
    #region Functions

    /// <summary>
    /// Serializes the batches' diagnostics as UTF-8 JSON.
    /// </summary>
    /// <param name="batches">The result batches of one run, in any order.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultBatchData> batches)
    {
        var ordered = batches
            .Select(batch => (Batch: batch, First: batch.Results.Count == 0 ? int.MaxValue : batch.Results.Min(r => r.TaskIndex)))
            .OrderBy(entry => entry.First)
            .ThenBy(entry => entry.Batch.Diagnostics?.RuntimeTarget ?? "", StringComparer.Ordinal)
            .ThenBy(entry => entry.Batch.Diagnostics?.WallMs ?? 0)
            .ThenBy(entry => entry.Batch.Results.Sum(result => result.FuelConsumed))
            .Select(entry => entry.Batch)
            .ToList();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("batches");
            foreach (var batch in ordered)
                WriteBatch(writer, batch);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteBatch(Utf8JsonWriter writer, VerifyResultBatchData batch)
    {
        var diagnostics = batch.Diagnostics;
        var results = batch.Results.OrderBy(result => result.TaskIndex).ToList();

        writer.WriteStartObject();
        if (diagnostics != null)
        {
            writer.WriteString("runtime_target", diagnostics.RuntimeTarget);
            writer.WriteString("wasmtime", diagnostics.WasmtimeVersion);
            writer.WriteString("controller", diagnostics.ControllerVersion);
            writer.WriteString("runtime", diagnostics.RuntimeId);
            writer.WriteString("runtime_sha256", diagnostics.RuntimeSha256);
            writer.WriteNumber("degree", diagnostics.Degree);
            writer.WriteNumber("wall_ms", diagnostics.WallMs);
        }

        writer.WriteNumber("fuel", results.Sum(result => result.FuelConsumed));

        writer.WriteStartArray("tasks");
        foreach (var result in results)
            writer.WriteNumberValue(result.TaskIndex);
        writer.WriteEndArray();

        // The per-task metrics: what a run cost, never what it computed (that is the report's).
        writer.WriteStartArray("metrics");
        foreach (var result in results)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", result.TaskIndex);
            writer.WriteNumber("fuel", result.FuelConsumed);
            writer.WriteNumber("peak_memory", result.PeakMemoryBytes);
            writer.WriteNumber("wall_ms", result.WallMs);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    #endregion
}
