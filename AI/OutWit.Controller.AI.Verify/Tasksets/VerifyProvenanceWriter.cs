using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the provenance document of a run: one entry per result batch (node, wasmtime, runtime
/// module and hash, degree, wall time, the task indices it covered and each task's metrics —
/// fuel, peak memory, wall time) and one per node, aggregated. Batches are ordered by their
/// first task index so the document is stable for a stable placement; the metrics make it a
/// record, not an identity — the verdict report is the identity.
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
            .ThenBy(entry => entry.Batch.Diagnostics?.NodeName ?? "", StringComparer.Ordinal)
            .Select(entry => entry.Batch)
            .ToList();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("nodes");
            foreach (var node in Nodes(ordered))
                WriteNode(writer, node);
            writer.WriteEndArray();

            writer.WriteStartArray("batches");
            foreach (var batch in ordered)
                WriteBatch(writer, batch);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static IEnumerable<NodeSummary> Nodes(IReadOnlyList<VerifyResultBatchData> ordered)
    {
        return ordered
            .Where(batch => batch.Diagnostics != null)
            .GroupBy(batch => (batch.Diagnostics!.NodeId, batch.Diagnostics.NodeName))
            .Select(group =>
            {
                var first = group.First().Diagnostics!;
                var runtimes = group
                    .Where(batch => batch.Diagnostics!.RuntimeSha256.Length > 0)
                    .GroupBy(batch => batch.Diagnostics!.RuntimeId, StringComparer.Ordinal)
                    .OrderBy(runtime => runtime.Key, StringComparer.Ordinal)
                    .Select(runtime => (runtime.Key, runtime.First().Diagnostics!.RuntimeSha256))
                    .ToList();

                return new NodeSummary(first, group.Count(), group.Sum(batch => batch.Results.Count),
                    group.Sum(batch => batch.Results.Sum(result => result.FuelConsumed)), runtimes);
            })
            .OrderBy(node => node.First.NodeName, StringComparer.Ordinal)
            .ThenBy(node => node.First.NodeId);
    }

    private static void WriteNode(Utf8JsonWriter writer, NodeSummary node)
    {
        writer.WriteStartObject();
        writer.WriteString("node_id", node.First.NodeId);
        writer.WriteString("node_name", node.First.NodeName);
        writer.WriteString("runtime_target", node.First.RuntimeTarget);
        writer.WriteString("wasmtime", node.First.WasmtimeVersion);
        writer.WriteString("native_library", node.First.NativeLibraryPath);
        writer.WriteString("controller", node.First.ControllerVersion);
        writer.WriteNumber("batches", node.Batches);
        writer.WriteNumber("tasks", node.Tasks);
        writer.WriteNumber("fuel", node.Fuel);
        writer.WriteStartObject("runtimes");
        foreach (var (runtimeId, sha256) in node.Runtimes)
            writer.WriteString(runtimeId, sha256);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteBatch(Utf8JsonWriter writer, VerifyResultBatchData batch)
    {
        var diagnostics = batch.Diagnostics;
        var results = batch.Results.OrderBy(result => result.TaskIndex).ToList();

        writer.WriteStartObject();
        if (diagnostics != null)
        {
            writer.WriteString("node_id", diagnostics.NodeId);
            writer.WriteString("node_name", diagnostics.NodeName);
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

    #region Types

    private sealed record NodeSummary(
        VerifyBatchDiagnosticsData First,
        int Batches,
        int Tasks,
        long Fuel,
        IReadOnlyList<(string RuntimeId, string Sha256)> Runtimes);

    #endregion
}
