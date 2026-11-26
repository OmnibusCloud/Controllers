using System.Text.Json;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Diagnostics;

/// <summary>
/// The capabilities document: what a lab can know before a run. The controller's version and its
/// solver profile, the queries, the defaults and ceilings of a run's options, the ceilings of a
/// problem and of a problem set, the words of the documents of a run (those a row of the results
/// may say, the codes of a package a node could not work on among them, and those of the report
/// and the integrity document), the size of a package, what a node runs a package with and the
/// least memory it must have, the benchmark's unit, the platforms, and the pool as totals. Nodes
/// run the same module as the host (a node on another version refuses the work), so what is said
/// here is the pool's. A JSON document, so an application reads it without the controller's types
/// and reads its limits and its words here instead of carrying copies of them; its schema is
/// <c>docs/smt-capabilities.schema.json</c>. Every list of words is the code's own list, the one
/// the documents are written with.
/// </summary>
internal static class SmtCapabilitiesWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "smt-capabilities/1";

    /// <summary>The platforms a node runs the controller on.</summary>
    public static readonly IReadOnlyList<string> PLATFORMS = ["win-x64", "linux-x64", "osx-arm64"];

    /// <summary>The queries, in the words the documents write them in.</summary>
    public static readonly IReadOnlyList<string> QUERIES = [.. Enum.GetValues<SmtQuery>().Select(SmtResultsWriter.Query)];

    /// <summary>The words of a row's expected verdict, as the results write them; <c>none</c> last.</summary>
    public static readonly IReadOnlyList<string> EXPECTED =
    [
        .. Enum.GetValues<SmtProblemStatus>().Select(SmtResultsWriter.Expected).Distinct(StringComparer.Ordinal).OrderBy(word => word == SmtResultWords.NONE)
    ];

    /// <summary>The codes of a refused row (and the keys of the report's <c>refused_by_code</c>), in ordinal order.</summary>
    public static readonly IReadOnlyList<string> REFUSAL_CODES = [.. SmtRefusalCodes.All.Order(StringComparer.Ordinal)];

    #endregion

    #region Functions

    /// <summary>Serializes the document as UTF-8 JSON.</summary>
    /// <param name="diagnostics">The diagnostics of the host's module.</param>
    /// <param name="pool">The pool's totals, or null when the host cannot say.</param>
    /// <returns>The document's bytes.</returns>
    public static byte[] Write(SmtRuntimeDiagnosticsData diagnostics, SmtPoolSummary? pool)
    {
        var defaults = SmtOptionsRules.Resolve(new SmtOptionsData());

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ControllerBuildInfo.VERSION);

            writer.WriteStartObject("profile");
            writer.WriteString("id", SmtProfile.ID);
            writer.WriteString("solver", "z3");
            writer.WriteString("solver_version", SmtProfile.SOLVER_VERSION);
            writer.WriteString("module", SmtModuleLocator.RelativePath);
            writer.WriteString("module_sha256", SmtProfile.MODULE_SHA256);
            writer.WriteNumber("module_size", SmtProfile.MODULE_SIZE);
            writer.WriteBoolean("module_present", diagnostics.ModulePresent);
            writer.WriteString("engine", $"wasmtime {WasmHostNatives.VERSION}");
            writer.WriteEndObject();

            WriteWords(writer, "queries", QUERIES);

            writer.WriteStartObject("defaults");
            writer.WriteNumber("max_models", defaults.MaxModels);
            writer.WriteNumber("seed", defaults.Seed);
            writer.WriteNumber("rlimit", SmtProfile.DEFAULT_RESOURCE_LIMIT);
            writer.WriteNumber("hard_cap_s", defaults.HardCapSeconds);
            writer.WriteNumber("memory_mb", defaults.MemoryMegabytes);
            writer.WriteNumber("sampling_rate", SmtOptionsRules.DEFAULT_SAMPLING_RATE);
            writer.WriteEndObject();

            writer.WriteStartObject("ceilings");
            writer.WriteNumber("max_models", SmtOptionsRules.MAX_MODELS);
            writer.WriteNumber("seed", SmtOptionsRules.MAX_SEED);
            writer.WriteNumber("rlimit", SmtOptionsRules.MAX_RESOURCE_LIMIT);
            writer.WriteNumber("hard_cap_s", SmtOptionsRules.MAX_HARD_CAP_SECONDS);
            writer.WriteNumber("memory_mb", SmtOptionsRules.MAX_MEMORY_MEGABYTES);
            writer.WriteNumber("sampling_rate", SmtOptionsRules.MAX_SAMPLING_RATE);
            writer.WriteEndObject();

            // The budgets of a session are counts: the hard cap is fuel at the reference rate, and
            // the clock is a node's own backstop, never a verdict.
            writer.WriteStartObject("time");
            writer.WriteNumber("fuel_per_second", SmtProfile.FUEL_PER_SECOND);
            writer.WriteNumber("backstop_base_ms", SmtProfile.BACKSTOP_BASE_MS);
            writer.WriteNumber("backstop_slowdown", SmtProfile.BACKSTOP_SLOWDOWN);
            writer.WriteEndObject();

            writer.WriteStartObject("session");
            writer.WriteNumber("output_bytes", SmtProfile.OUTPUT_LIMIT_BYTES);
            writer.WriteNumber("transcript_bytes", SmtTranscript.LIMIT_BYTES);
            writer.WriteNumber("message_characters", SmtResultWords.MAX_MESSAGE_CHARACTERS);
            writer.WriteNumber("memory_headroom_bytes", SmtProfile.HOST_MEMORY_HEADROOM_BYTES);
            writer.WriteEndObject();

            writer.WriteStartObject("problem");
            writer.WriteNumber("max_characters", SmtReaderLimits.PROBLEM_CHARACTERS);
            writer.WriteNumber("max_tokens", SmtReaderLimits.PROBLEM_TOKENS);
            writer.WriteNumber("max_depth", SmtReaderLimits.PROBLEM_DEPTH);
            writer.WriteNumber("max_numeral_digits", SmtReaderLimits.PROBLEM_NUMERAL_DIGITS);
            writer.WriteNumber("max_literal_bits", SmtReaderLimits.PROBLEM_LITERAL_BITS);
            writer.WriteNumber("max_string_characters", SmtReaderLimits.PROBLEM_STRING_CHARACTERS);
            writer.WriteNumber("max_declared_symbols", SmtSanitizer.MAX_DECLARED_SYMBOLS);
            writer.WriteNumber("max_index", SmtSanitizer.MAX_INDEX);
            writer.WriteNumber("max_term_characters", SmtSanitizer.TERM_CHARACTERS);
            writer.WriteEndObject();

            writer.WriteStartObject("problem_set");
            writer.WriteNumber("max_line_bytes", SmtProblemSetLimits.MAX_LINE_BYTES);
            writer.WriteNumber("max_rows", SmtProblemSetLimits.MAX_ROWS);
            writer.WriteNumber("max_id_characters", SmtProblemSetLimits.MAX_ID_CHARACTERS);
            writer.WriteNumber("max_values", SmtProblemSetLimits.MAX_VALUES);
            writer.WriteNumber("max_project", SmtProblemSetLimits.MAX_PROJECT);
            writer.WriteNumber("max_tags_characters", SmtProblemSetLimits.MAX_TAGS_CHARACTERS);
            writer.WriteEndObject();

            // The words of a row of the results: the statuses of the results (a node's fault is
            // never one of them; the server's no_result and disputed are), the code of a row by its
            // status, and every other word a row writes.
            writer.WriteStartObject("results");
            WriteWords(writer, "statuses", SmtRunWords.STATUSES);
            writer.WriteStartObject("codes");
            WriteWords(writer, SmtResultWords.REFUSED, REFUSAL_CODES);
            WriteWords(writer, SmtResultWords.ENGINE_CRASH, SmtSolveCodes.CRASHES);
            WriteWords(writer, SmtRunWords.NO_RESULT, SmtRunSettlement.NO_RESULT_REASONS);
            WriteWords(writer, SmtRunWords.DISPUTED, SmtRunWords.DISPUTED_CODES);
            writer.WriteEndObject();
            WriteWords(writer, "verdicts", SmtResultWords.VERDICTS);
            WriteWords(writer, "reasons", SmtResultWords.REASONS);
            WriteWords(writer, "proofs", SmtResultWords.PROOFS);
            WriteWords(writer, "expected", EXPECTED);
            WriteWords(writer, "expected_agreements", SmtRunWords.EXPECTATIONS);
            WriteWords(writer, "validations", SmtRunWords.VALIDATIONS);
            WriteWords(writer, "validation_reasons", SmtModelCheck.REASONS);
            WriteWords(writer, "settled_by", SmtRunWords.SETTLED_BY);
            WriteWords(writer, "node_faults", SmtNodeFaults.ALL);
            writer.WriteEndObject();

            // The words of the report and the integrity document beside a row's.
            writer.WriteStartObject("run");
            WriteWords(writer, "sampling", SmtSampling.STATES);
            WriteWords(writer, "bindings", SmtRunWords.BINDINGS);
            WriteWords(writer, "rounds", SmtRound.NAMES);
            WriteWords(writer, "repair_reasons", SmtRunSettlement.REPAIR_REASONS);
            WriteWords(writer, "rejected", SmtRoundIntake.REASONS);
            WriteWords(writer, "dispute_causes", SmtRunWords.DISPUTE_CAUSES);
            WriteWords(writer, "dispute_outcomes", SmtRunWords.DISPUTE_OUTCOMES);
            WriteWords(writer, "stopped_by", SmtReportFacts.BUDGETS);
            writer.WriteEndObject();

            writer.WriteStartObject("packages");
            writer.WriteNumber("max_problems", SmtPackageCodec.MAX_PACKAGE_PROBLEMS);
            writer.WriteNumber("max_bytes", SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES);
            writer.WriteNumber("max_problem_frame_bytes", SmtPackageCodec.MAX_PROBLEM_FRAME_BYTES);
            writer.WriteNumber("max_result_frame_bytes", SmtPackageCodec.MAX_RESULT_FRAME_BYTES);
            writer.WriteEndObject();

            // What a node runs a package with: one session a core, at most max_degree, as many as
            // ram_headroom of its available memory holds at two caps of the linear memory and the
            // margin a session; and the least memory a machine has for the planner to send it one.
            writer.WriteStartObject("node");
            writer.WriteNumber("min_ram_mb", SmtSandboxDegree.MIN_RAM_MB);
            writer.WriteNumber("max_degree", SmtSandboxDegree.MAX_DEGREE);
            writer.WriteNumber("ram_headroom", SmtSandboxDegree.RAM_HEADROOM);
            writer.WriteNumber("session_margin_bytes", SmtSandboxDegree.SESSION_MARGIN_BYTES);
            writer.WriteNumber("session_bytes", SmtSandboxDegree.SessionBytes(new SmtOptionsData()));
            writer.WriteString("priority", "below_normal");
            writer.WriteEndObject();

            writer.WriteString("benchmark_unit", SmtBenchmark.UNIT);
            WriteWords(writer, "platforms", PLATFORMS);

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

    private static void WriteWords(Utf8JsonWriter writer, string name, IReadOnlyList<string> words)
    {
        writer.WriteStartArray(name);
        foreach (var word in words)
            writer.WriteStringValue(word);
        writer.WriteEndArray();
    }

    #endregion
}
