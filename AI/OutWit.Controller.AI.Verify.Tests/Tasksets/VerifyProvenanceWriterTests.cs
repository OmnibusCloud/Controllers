using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The provenance document: batches ordered by first task, the round, the platform and module of
/// each, no machine named or measurable, and no dependence on the order the batches came back in.
/// </summary>
[TestFixture]
public sealed class VerifyProvenanceWriterTests
{
    #region Tests

    [Test]
    public void BatchesAreOrderedByFirstTaskAndCarryTheirPlatformTest()
    {
        var late = Batch("python-3.14.6", "aaa", [4, 5]);
        var early = Batch("quickjs-0.15.1", "bbb", [0, 1, 2]);
        var middle = Batch("quickjs-0.15.1", "bbb", [3]);

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, late, early, middle)]));
        var batches = document.RootElement.GetProperty("batches").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo(VerifyProvenanceWriter.SCHEMA));
            Assert.That(batches.Select(batch => batch.GetProperty("tasks")[0].GetInt32()), Is.EqualTo(new[] { 0, 3, 4 }));
            Assert.That(batches[0].GetProperty("round").GetString(), Is.EqualTo("main"));
            Assert.That(batches[0].GetProperty("runtime_target").GetString(), Is.EqualTo("linux-x64"));
            Assert.That(batches[0].GetProperty("wasmtime").GetString(), Is.EqualTo("48.0.2.0"));
            Assert.That(batches[0].GetProperty("controller").GetString(), Is.EqualTo("0.2.0-test"));
            Assert.That(batches[0].GetProperty("runtime").GetString(), Is.EqualTo("quickjs-0.15.1"));
            Assert.That(batches[0].GetProperty("runtime_sha256").GetString(), Is.EqualTo("bbb"));
        });
    }

    [Test]
    public void TheDocumentNamesNoMachineTest()
    {
        var batch = Batch("python-3.14.6", "aaa", [0]);
        batch.Diagnostics.NodeId = Guid.NewGuid();
        batch.Diagnostics.NodeName = "contributors-laptop";
        batch.Diagnostics.NativeLibraryPath = "/home/contributor/lib";

        var json = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, batch)]));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain("/home/contributor"));
            Assert.That(json, Does.Not.Contain(batch.Diagnostics.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("\"nodes\""));
        });
    }

    [Test]
    public void L6TheDocumentGivesNothingAMachineCanBeMeasuredByTest()
    {
        // The degree a batch ran at is min(cores, 0.75 x RAM / memory cap, MaxDegree): with the
        // cap and MaxDegree the lab's to choose, a node's cores and memory could be read off it.
        var batch = Batch("python-3.14.6", "aaa", [0]);
        batch.Diagnostics.Degree = 15;
        batch.Diagnostics.WallMs = 123456;

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, batch)]));
        var entry = document.RootElement.GetProperty("batches")[0];

        Assert.Multiple(() =>
        {
            Assert.That(entry.TryGetProperty("degree", out _), Is.False, "how many programs a node ran at once is not said");
            Assert.That(entry.TryGetProperty("wall_ms", out _), Is.False, "nor how long the batch took as a whole, from which the same could be worked out");
            Assert.That(entry.GetRawText(), Does.Not.Contain("123456"));
        });
    }

    [Test]
    public void OrderOfArrivalDoesNotChangeTheDocumentTest()
    {
        var first = Batch("python-3.14.6", "aaa", [0, 1]);
        var second = Batch("python-3.14.6", "aaa", [2, 3]);

        var one = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, first, second)]));
        var two = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, second, first)]));

        Assert.That(two, Is.EqualTo(one));
    }

    [Test]
    public void TwoExecutionsOfOneBatchAreBothListedInAStableOrderTest()
    {
        var fast = Batch("python-3.14.6", "aaa", [0, 1], wallMs: 5);
        var slow = Batch("python-3.14.6", "aaa", [0, 1], wallMs: 50);

        var one = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, fast, slow)]));
        var two = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, slow, fast)]));

        using var document = JsonDocument.Parse(one);

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(document.RootElement.GetProperty("batches").GetArrayLength(), Is.EqualTo(2));
        });
    }

    [Test]
    public void EveryRoundIsListedUnderItsNameTest()
    {
        var rounds = new[]
        {
            Round(VerifyRound.MAIN, Batch("python-3.14.6", "aaa", [0, 1])),
            Round(VerifyRound.REPAIR, Batch("python-3.14.6", "aaa", [1])),
            Round(VerifyRound.DISPUTE, Batch("python-3.14.6", "aaa", [0]))
        };

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write(rounds));
        var names = document.RootElement.GetProperty("batches").EnumerateArray().Select(batch => batch.GetProperty("round").GetString()).ToList();

        Assert.That(names, Is.EquivalentTo(new[] { "main", "repair", "dispute" }));
    }

    [Test]
    public void BatchCarriesItsTasksMetricsTest()
    {
        var batch = new VerifyAcceptedBatch(Diagnostics("python-3.14.6", "aaa"),
        [
            VerifyComparable.Record(new VerifyResultData { TaskIndex = 3, FuelConsumed = 300, PeakMemoryBytes = 65536, WallMs = 30 }),
            VerifyComparable.Record(new VerifyResultData { TaskIndex = 1, FuelConsumed = 100, PeakMemoryBytes = 131072, WallMs = 10 })
        ]);

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([Round(VerifyRound.MAIN, batch)]));
        var entry = document.RootElement.GetProperty("batches")[0];
        var metrics = entry.GetProperty("metrics").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(entry.GetProperty("fuel").GetInt64(), Is.EqualTo(400));
            Assert.That(entry.GetProperty("tasks").EnumerateArray().Select(task => task.GetInt32()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(metrics.Select(metric => metric.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(metrics[0].GetProperty("fuel").GetInt64(), Is.EqualTo(100));
            Assert.That(metrics[0].GetProperty("peak_memory").GetInt64(), Is.EqualTo(131072));
            Assert.That(metrics[0].GetProperty("wall_ms").GetInt32(), Is.EqualTo(10));
            Assert.That(metrics[1].GetProperty("fuel").GetInt64(), Is.EqualTo(300));
        });
    }

    #endregion

    #region Tools

    private static VerifyRound Round(string name, params VerifyAcceptedBatch[] batches)
    {
        var unit = new VerifyTaskPackageData { Ticket = new string('a', 32) };
        var round = new VerifyRound { Name = name, Issued = [unit] };
        foreach (var batch in batches)
            round.Accepted.Add(new VerifyAcceptedPackage(unit, "", [batch]));

        return round;
    }

    private static VerifyAcceptedBatch Batch(string runtimeId, string sha256, int[] tasks, int wallMs = 12)
    {
        return new VerifyAcceptedBatch(Diagnostics(runtimeId, sha256),
            tasks.Select(index => VerifyComparable.Record(new VerifyResultData { TaskIndex = index, Verdict = VerifyVerdict.Pass, WallMs = wallMs })).ToList());
    }

    private static VerifyBatchDiagnosticsData Diagnostics(string runtimeId, string sha256)
    {
        return new VerifyBatchDiagnosticsData
        {
            RuntimeTarget = "linux-x64",
            WasmtimeVersion = "48.0.2.0",
            RuntimeId = runtimeId,
            RuntimeSha256 = sha256,
            Degree = 2,
            WallMs = 12,
            ControllerVersion = "0.2.0-test"
        };
    }

    #endregion
}
