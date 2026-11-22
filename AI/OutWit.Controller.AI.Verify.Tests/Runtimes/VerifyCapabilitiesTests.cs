using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Runtimes;

/// <summary>
/// The capabilities document and the pool's totals: what a submitter can know before a run,
/// with no node named or told apart.
/// </summary>
[TestFixture]
public sealed class VerifyCapabilitiesTests
{
    #region Pool Tests

    [Test]
    public void ThePoolIsAddedUpTest()
    {
        var summary = VerifyPoolProbe.Summarize(
        [
            Measured(rate: 23.5, degree: 16),
            Measured(rate: 3.25, degree: 2),
            Measured(rate: 3.25, degree: 2)
        ]);

        Assert.That(summary, Is.EqualTo(new VerifyPoolSummary(Nodes: 3, Measured: 3, Workers: 20, Rate: 30.0)));
    }

    [Test]
    public void ANodeThatHasNotMeasuredItselfCountsAsANodeOnlyTest()
    {
        var summary = VerifyPoolProbe.Summarize(
        [
            Measured(rate: 10, degree: 8),
            WitBenchmarkResult.Default,
            new WitBenchmarkResult { Rate = 99, Unit = "another-unit" }
        ]);

        Assert.That(summary, Is.EqualTo(new VerifyPoolSummary(Nodes: 3, Measured: 1, Workers: 8, Rate: 10)));
    }

    [Test]
    public void TheWorkersAreNotGuessedWhenTheHostDoesNotCarryThemTest()
    {
        // The cloud server keeps a benchmark's rate and unit, not its custom values: the
        // degree of a node does not reach the host. A total over the nodes that happen to
        // carry it would read as the pool's, and a zero would read as "no workers".
        var summary = VerifyPoolProbe.Summarize(
        [
            Measured(rate: 10, degree: 8),
            new WitBenchmarkResult { Rate = 5, Unit = VerifyBenchmark.UNIT }
        ]);

        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(new VerifyRuntimeDiagnosticsData(), summary));

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.EqualTo(new VerifyPoolSummary(Nodes: 2, Measured: 2, Workers: null, Rate: 15)));
            Assert.That(document.RootElement.GetProperty("pool").TryGetProperty("workers", out _), Is.False);
            Assert.That(document.RootElement.GetProperty("pool").GetProperty("rate").GetDouble(), Is.EqualTo(15));
        });
    }

    [Test]
    public void AProcessWithoutANodesManagerCannotSayTest()
    {
        // Not an async test on purpose: awaiting a task of a controller type puts that type into
        // the test's state machine, the test loader then pulls the controller assembly into the
        // default load context, and the module isolation proof (VerifyModuleLoadTests) is lost.
        Assert.That(VerifyPoolProbe.SummarizeAsync(null).GetAwaiter().GetResult(), Is.Null);
    }

    #endregion

    #region Document Tests

    [Test]
    public void TheDocumentSaysVersionRuntimesLimitsAndThePoolTest()
    {
        var diagnostics = new VerifyRuntimeDiagnosticsData
        {
            WasmtimeVersion = "48.0.2.0",
            Runtimes =
            [
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6, Sha256 = "aa", Available = true },
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.QUICKJS_0_15_1, Sha256 = "bb", Available = false },
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.WASI, Sha256 = "", Available = true }
            ]
        };

        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(diagnostics, new VerifyPoolSummary(6, 5, 76, 57.3456)));
        var root = document.RootElement;
        var runtimes = root.GetProperty("runtimes").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo(VerifyCapabilitiesWriter.SCHEMA));
            Assert.That(root.GetProperty("controller").GetString(), Does.Match("^[0-9]+[.][0-9]+[.][0-9]+"));
            Assert.That(root.GetProperty("wasmtime").GetString(), Is.EqualTo("48.0.2.0"));
            Assert.That(root.GetProperty("benchmark_unit").GetString(), Is.EqualTo(VerifyBenchmark.UNIT));
            Assert.That(runtimes.Select(r => r.GetProperty("language").GetString()), Is.EqualTo(new[] { "python", "javascript", "wasi" }));
            Assert.That(runtimes[1].GetProperty("available").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("defaults").GetProperty("batch_size").GetInt32(), Is.EqualTo(32));
            Assert.That(root.GetProperty("defaults").GetProperty("max_degree").GetInt32(), Is.EqualTo(16));
            Assert.That(root.GetProperty("ceilings").GetProperty("memory_bytes").GetInt64(), Is.EqualTo(VerifyLimitCeilings.MAX_MEMORY_BYTES));
            Assert.That(root.GetProperty("packages").GetProperty("target").GetInt32(), Is.EqualTo(1024));
            Assert.That(root.GetProperty("pool").GetProperty("nodes").GetInt32(), Is.EqualTo(6));
            Assert.That(root.GetProperty("pool").GetProperty("measured").GetInt32(), Is.EqualTo(5));
            Assert.That(root.GetProperty("pool").GetProperty("workers").GetInt32(), Is.EqualTo(76));
            Assert.That(root.GetProperty("pool").GetProperty("rate").GetDouble(), Is.EqualTo(57.346));
        });
    }

    [Test]
    public void B2TheDocumentCarriesTheBoundsOfATasksetAndOfAPreflightTest()
    {
        // Hardening batch B, item B2: the bounds on what the server reads from a lab were in the
        // schema's prose and in the code only, so an application had to carry copies of them.
        // They are a section of the document, and the document is a new version for it.
        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(new VerifyRuntimeDiagnosticsData(), null));
        var root = document.RootElement;

        Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("verify-capabilities/3"));
        Assert.That(root.TryGetProperty("taskset", out var taskset), Is.True, "the bounds of a taskset are a section of the document");
        Assert.That(taskset.EnumerateObject().Select(field => (field.Name, field.Value.GetInt64())), Is.EqualTo(new (string, long)[]
        {
            ("max_line_bytes", VerifyTasksetLimits.MAX_LINE_BYTES),
            ("max_tasks", VerifyTasksetLimits.MAX_TASKS),
            ("max_sources", VerifyTasksetLimits.MAX_SOURCES),
            ("max_args", VerifyTasksetLimits.MAX_ARGS),
            ("max_suite_cases", VerifyTasksetLimits.MAX_SUITE_CASES),
            ("max_preflight_messages", VerifyPreflightReport.MaxMessages),
            ("max_preflight_messages_per_kind", VerifyTasksetLimits.MAX_MESSAGES),
            ("max_preflight_message_length", VerifyTasksetLimits.MAX_MESSAGE_LENGTH),
            ("max_preflight_unknown_runtimes", VerifyTasksetLimits.MAX_LISTED_RUNTIMES)
        }));
    }

    [Test]
    public void B2EveryFieldOfTheSecondVersionIsStillThereTest()
    {
        // The third version adds a section and takes nothing away: an application written for
        // verify-capabilities/2 finds every field it read, under the name it had.
        var diagnostics = new VerifyRuntimeDiagnosticsData
        {
            Runtimes = [new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, Sha256 = "aa", Available = true }]
        };

        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(diagnostics, new VerifyPoolSummary(1, 1, 1, 1)));
        var root = document.RootElement;
        string[] Names(JsonElement element) => element.EnumerateObject().Select(field => field.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Names(root), Is.SupersetOf(new[] { "schema", "controller", "wasmtime", "benchmark_unit", "runtimes", "defaults", "ceilings", "time", "sources", "modules", "packages", "pool" }));
            Assert.That(Names(root.GetProperty("runtimes")[0]), Is.EqualTo(new[] { "id", "language", "sha256", "available", "notes" }));
            Assert.That(Names(root.GetProperty("defaults")), Is.EqualTo(new[] { "batch_size", "max_degree", "fuel", "memory_bytes", "stdout_bytes", "stderr_bytes" }));
            Assert.That(Names(root.GetProperty("ceilings")), Is.EqualTo(new[] { "fuel", "memory_bytes", "stdout_bytes", "stderr_bytes" }));
            Assert.That(Names(root.GetProperty("time")), Is.EqualTo(new[] { "reference_fuel_per_second", "backstop_base_ms", "backstop_fuel_per_second" }));
            Assert.That(Names(root.GetProperty("sources")), Is.EqualTo(new[] { "name_pattern", "max_name_bytes", "max_depth" }));
            Assert.That(Names(root.GetProperty("modules")), Is.EqualTo(new[] { "max_bytes", "max_nesting", "compile_fuel_per_byte" }));
            Assert.That(Names(root.GetProperty("packages")), Is.EqualTo(new[] { "target", "max_tasks", "max_bytes", "max_output_bytes", "max_batch_tasks", "max_batch_output_bytes" }));
            Assert.That(Names(root.GetProperty("pool")), Is.EqualTo(new[] { "nodes", "measured", "workers", "rate" }));
        });
    }

    [Test]
    public void WhatAnAuthorShouldKnowTravelsWithItsRuntimeTest()
    {
        var diagnostics = new VerifyRuntimeDiagnosticsData
        {
            Runtimes =
            [
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6, Sha256 = "aa", Available = true },
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6_NUMPY_2_5_4, Sha256 = "bb", Available = true },
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0, Sha256 = "cc", Available = true },
                new VerifyRuntimeInfoData { RuntimeId = VerifyRuntimeCatalog.TYPESCRIPT_SUCRASE_3_35_1, Sha256 = "dd", Available = true }
            ]
        };

        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(diagnostics, null));
        var runtimes = document.RootElement.GetProperty("runtimes").EnumerateArray().ToList();
        string Notes(int index) => runtimes[index].TryGetProperty("notes", out var notes)
            ? string.Join(" ", notes.EnumerateArray().Select(note => note.GetString()))
            : "";

        Assert.Multiple(() =>
        {
            Assert.That(runtimes[0].TryGetProperty("notes", out _), Is.False, "nothing beyond the language: no notes");
            Assert.That(Notes(1), Does.Contain("32-bit").And.Contain("dtype=np.int64").And.Contain("No BLAS").And.Contain("binary128"));
            Assert.That(Notes(2), Does.Contain("without exceptions").And.Contain("long").And.Contain("recursion"));
            Assert.That(Notes(3), Does.Contain("not checked"));
        });
    }

    [Test]
    public void AHostThatCannotSayLeavesThePoolOutTest()
    {
        using var document = JsonDocument.Parse(VerifyCapabilitiesWriter.Write(new VerifyRuntimeDiagnosticsData(), null));

        Assert.That(document.RootElement.TryGetProperty("pool", out _), Is.False);
    }

    [Test]
    public void TheDocumentNamesNoMachineTest()
    {
        var json = System.Text.Encoding.UTF8.GetString(VerifyCapabilitiesWriter.Write(new VerifyRuntimeDiagnosticsData { RuntimeTarget = "windows-x64" }, new VerifyPoolSummary(1, 1, 16, 20)));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain(Environment.MachineName));
            Assert.That(json, Does.Not.Contain("windows-x64"), "the host's own platform is not the pool's");
            Assert.That(json, Does.Not.Contain("node_id"));
        });
    }

    #endregion

    #region Tools

    private static IWitBenchmarkResult Measured(double rate, int degree)
    {
        return new WitBenchmarkResult
        {
            Rate = rate,
            Unit = VerifyBenchmark.UNIT,
            Custom = new Dictionary<string, string> { ["degree"] = degree.ToString() }
        };
    }

    #endregion
}
