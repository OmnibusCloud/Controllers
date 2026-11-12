using System.Text.Json;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The capabilities document: what a submitter can know before a run. The controller's
/// version, the runtimes this version carries (a node runs the same module: the platform
/// admits no other version to the work), the defaults and the ceilings a taskset is held to,
/// and the pool as totals. A JSON document like every other output, so an application reads
/// it without the controller's types.
/// </summary>
public static class VerifyCapabilitiesWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "verify-capabilities/1";

    #endregion

    #region Functions

    /// <summary>Serializes the document as UTF-8 JSON.</summary>
    /// <param name="diagnostics">The runtime inventory of the host's module.</param>
    /// <param name="pool">The pool's totals, or null when the host cannot say.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(VerifyRuntimeDiagnosticsData diagnostics, VerifyPoolSummary? pool)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ControllerBuildInfo.VERSION);
            writer.WriteString("wasmtime", diagnostics.WasmtimeVersion);
            writer.WriteString("benchmark_unit", VerifyBenchmark.UNIT);

            writer.WriteStartArray("runtimes");
            foreach (var runtime in diagnostics.Runtimes)
            {
                writer.WriteStartObject();
                writer.WriteString("id", runtime.RuntimeId);
                writer.WriteString("language", Language(runtime.RuntimeId));
                writer.WriteString("sha256", runtime.Sha256);
                writer.WriteBoolean("available", runtime.Available);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("defaults");
            writer.WriteNumber("batch_size", VerifyTasksetPlanner.DEFAULT_BATCH_SIZE);
            writer.WriteNumber("max_degree", VerifySandboxDegree.DEFAULT_MAX_DEGREE);
            writer.WriteNumber("fuel", VerifySandboxDefaults.DEFAULT_FUEL_BUDGET);
            writer.WriteNumber("memory_bytes", VerifySandboxDefaults.DEFAULT_MEMORY_BYTES);
            writer.WriteNumber("wall_ms", VerifySandboxDefaults.DEFAULT_WALL_TIME_MS);
            writer.WriteNumber("stdout_bytes", VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES);
            writer.WriteNumber("stderr_bytes", VerifySandboxDefaults.DEFAULT_STDERR_LIMIT_BYTES);
            writer.WriteEndObject();

            writer.WriteStartObject("ceilings");
            writer.WriteNumber("fuel", VerifyLimitCeilings.MAX_FUEL_BUDGET);
            writer.WriteNumber("memory_bytes", VerifyLimitCeilings.MAX_MEMORY_BYTES);
            writer.WriteNumber("wall_ms", VerifyLimitCeilings.MAX_WALL_TIME_MS);
            writer.WriteNumber("stdout_bytes", VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES);
            writer.WriteNumber("stderr_bytes", VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES);
            writer.WriteEndObject();

            writer.WriteStartObject("packages");
            writer.WriteNumber("target", VerifyPackagePlanner.TARGET_PACKAGES);
            writer.WriteNumber("max_tasks", VerifyPackagePlanner.MAX_PACKAGE_TASKS);
            writer.WriteNumber("max_bytes", VerifyPackagePlanner.MAX_PACKAGE_BYTES);
            writer.WriteEndObject();

            if (pool != null)
            {
                writer.WriteStartObject("pool");
                writer.WriteNumber("nodes", pool.Nodes);
                writer.WriteNumber("measured", pool.Measured);
                writer.WriteNumber("workers", pool.Workers);
                writer.WriteNumber("rate", Math.Round(pool.Rate, 3));
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string Language(string runtimeId)
    {
        return VerifyRuntimeCatalog.Find(runtimeId)?.Language switch
        {
            VerifyRuntimeLanguage.Python => "python",
            VerifyRuntimeLanguage.JavaScript => "javascript",
            VerifyRuntimeLanguage.Wasi => "wasi",
            _ => "unknown"
        };
    }

    #endregion
}
