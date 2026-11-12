using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
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
            WasmtimeVersion = "44.0.0.0",
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
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("verify-capabilities/1"));
            Assert.That(root.GetProperty("controller").GetString(), Does.Match("^[0-9]+[.][0-9]+[.][0-9]+"));
            Assert.That(root.GetProperty("wasmtime").GetString(), Is.EqualTo("44.0.0.0"));
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
