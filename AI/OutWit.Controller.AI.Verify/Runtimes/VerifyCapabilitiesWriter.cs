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
/// the bounds of a taskset itself and of what a preflight answers, and the pool as totals. A
/// JSON document like every other output, so an application reads it without the controller's
/// types, and reads its limits here instead of carrying copies of them.
/// <para>
/// <c>verify-capabilities/3</c> added the <c>taskset</c> section; every field of
/// <c>verify-capabilities/2</c> is there with the name and the meaning it had.
/// </para>
/// </summary>
public static class VerifyCapabilitiesWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "verify-capabilities/3";

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
                WriteNotes(writer, runtime.RuntimeId);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("defaults");
            writer.WriteNumber("batch_size", VerifyTasksetPlanner.DEFAULT_BATCH_SIZE);
            writer.WriteNumber("max_degree", VerifySandboxDegree.DEFAULT_MAX_DEGREE);
            writer.WriteNumber("fuel", VerifySandboxDefaults.DEFAULT_FUEL_BUDGET);
            writer.WriteNumber("memory_bytes", VerifySandboxDefaults.DEFAULT_MEMORY_BYTES);
            writer.WriteNumber("stdout_bytes", VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES);
            writer.WriteNumber("stderr_bytes", VerifySandboxDefaults.DEFAULT_STDERR_LIMIT_BYTES);
            writer.WriteEndObject();

            writer.WriteStartObject("ceilings");
            writer.WriteNumber("fuel", VerifyLimitCeilings.MAX_FUEL_BUDGET);
            writer.WriteNumber("memory_bytes", VerifyLimitCeilings.MAX_MEMORY_BYTES);
            writer.WriteNumber("stdout_bytes", VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES);
            writer.WriteNumber("stderr_bytes", VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES);
            writer.WriteEndObject();

            // Fuel is the one budget of a run's work; the clock is a node's own backstop.
            writer.WriteStartObject("time");
            writer.WriteNumber("reference_fuel_per_second", VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND);
            writer.WriteNumber("backstop_base_ms", VerifyBackstop.BASE_MS);
            writer.WriteNumber("backstop_fuel_per_second", VerifyBackstop.SLOWEST_FUEL_PER_SECOND);
            writer.WriteEndObject();

            writer.WriteStartObject("sources");
            writer.WriteString("name_pattern", VerifySourceNames.PATTERN);
            writer.WriteNumber("max_name_bytes", VerifySourceNames.MAX_NAME_BYTES);
            writer.WriteNumber("max_depth", VerifySourceNames.MAX_DEPTH);
            writer.WriteEndObject();

            // What a module a task brings (the raw WASI runtime) or the compiler builds is held to.
            writer.WriteStartObject("modules");
            writer.WriteNumber("max_bytes", VerifyWasmSandbox.MAX_TASK_MODULE_BYTES);
            writer.WriteNumber("max_nesting", VerifyModuleShape.MAX_NESTING);
            writer.WriteNumber("compile_fuel_per_byte", VerifyModuleShape.COMPILE_FUEL_PER_BYTE);
            writer.WriteEndObject();

            writer.WriteStartObject("packages");
            writer.WriteNumber("target", VerifyPackagePlanner.TARGET_PACKAGES);
            writer.WriteNumber("max_tasks", VerifyPackagePlanner.MAX_PACKAGE_TASKS);
            writer.WriteNumber("max_bytes", VerifyPackagePlanner.MAX_PACKAGE_BYTES);
            writer.WriteNumber("max_output_bytes", VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES);
            writer.WriteNumber("max_batch_tasks", VerifyBatchExecutor.MAX_BATCH_TASKS);
            writer.WriteNumber("max_batch_output_bytes", VerifyTasksetPlanner.MAX_BATCH_OUTPUT_BYTES);
            writer.WriteEndObject();

            // What the server reads from a lab is bounded where it is read: a taskset outside
            // these is refused when it is split, and a preflight answers within the last four.
            writer.WriteStartObject("taskset");
            writer.WriteNumber("max_line_bytes", VerifyTasksetLimits.MAX_LINE_BYTES);
            writer.WriteNumber("max_tasks", VerifyTasksetLimits.MAX_TASKS);
            writer.WriteNumber("max_sources", VerifyTasksetLimits.MAX_SOURCES);
            writer.WriteNumber("max_args", VerifyTasksetLimits.MAX_ARGS);
            writer.WriteNumber("max_suite_cases", VerifyTasksetLimits.MAX_SUITE_CASES);
            writer.WriteNumber("max_preflight_messages", VerifyPreflightReport.MaxMessages);
            writer.WriteNumber("max_preflight_messages_per_kind", VerifyTasksetLimits.MAX_MESSAGES);
            writer.WriteNumber("max_preflight_message_length", VerifyTasksetLimits.MAX_MESSAGE_LENGTH);
            writer.WriteNumber("max_preflight_unknown_runtimes", VerifyTasksetLimits.MAX_LISTED_RUNTIMES);
            writer.WriteEndObject();

            if (pool != null)
            {
                writer.WriteStartObject("pool");
                writer.WriteNumber("nodes", pool.Nodes);
                writer.WriteNumber("measured", pool.Measured);
                if (pool.Workers is { } workers)
                    writer.WriteNumber("workers", workers);
                writer.WriteNumber("rate", Math.Round(pool.Rate, 3));
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteNotes(Utf8JsonWriter writer, string runtimeId)
    {
        var notes = VerifyRuntimeCatalog.Find(runtimeId)?.Notes ?? [];
        if (notes.Count == 0)
            return;

        writer.WriteStartArray("notes");
        foreach (var note in notes)
            writer.WriteStringValue(note);
        writer.WriteEndArray();
    }

    private static string Language(string runtimeId)
    {
        return VerifyRuntimeCatalog.Find(runtimeId)?.Language switch
        {
            VerifyRuntimeLanguage.Python => "python",
            VerifyRuntimeLanguage.JavaScript => "javascript",
            VerifyRuntimeLanguage.Wasi => "wasi",
            VerifyRuntimeLanguage.Ruby => "ruby",
            VerifyRuntimeLanguage.Lua => "lua",
            VerifyRuntimeLanguage.TypeScript => "typescript",
            VerifyRuntimeLanguage.C => "c",
            _ => "unknown"
        };
    }

    #endregion
}
