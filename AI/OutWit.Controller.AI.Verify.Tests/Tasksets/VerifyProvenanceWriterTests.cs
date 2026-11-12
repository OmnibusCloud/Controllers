using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The provenance document: batches ordered by first task, the platform and module of each, no
/// machine named, and no dependence on the order the batches came back in.
/// </summary>
[TestFixture]
public sealed class VerifyProvenanceWriterTests
{
    [Test]
    public void BatchesAreOrderedByFirstTaskAndCarryTheirPlatformTest()
    {
        var late = Batch("python-3.14.6", "aaa", [4, 5]);
        var early = Batch("quickjs-0.15.1", "bbb", [0, 1, 2]);
        var middle = Batch("quickjs-0.15.1", "bbb", [3]);

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([late, early, middle]));
        var batches = document.RootElement.GetProperty("batches").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(batches.Select(batch => batch.GetProperty("tasks")[0].GetInt32()), Is.EqualTo(new[] { 0, 3, 4 }));
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
        batch.Diagnostics!.NodeId = Guid.NewGuid();
        batch.Diagnostics.NodeName = "contributors-laptop";
        batch.Diagnostics.NativeLibraryPath = "/home/contributor/lib";

        var json = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([batch]));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain("/home/contributor"));
            Assert.That(json, Does.Not.Contain(batch.Diagnostics.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("\"nodes\""));
        });
    }

    [Test]
    public void OrderOfArrivalDoesNotChangeTheDocumentTest()
    {
        var first = Batch("python-3.14.6", "aaa", [0, 1]);
        var second = Batch("python-3.14.6", "aaa", [2, 3]);

        var one = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([first, second]));
        var two = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([second, first]));

        Assert.That(two, Is.EqualTo(one));
    }

    [Test]
    public void TwoExecutionsOfOneBatchAreBothListedInAStableOrderTest()
    {
        var fast = Batch("python-3.14.6", "aaa", [0, 1]);
        fast.Diagnostics!.WallMs = 5;
        var slow = Batch("python-3.14.6", "aaa", [0, 1]);
        slow.Diagnostics!.WallMs = 50;

        var one = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([fast, slow]));
        var two = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([slow, fast]));

        using var document = JsonDocument.Parse(one);

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(document.RootElement.GetProperty("batches").GetArrayLength(), Is.EqualTo(2));
        });
    }

    [Test]
    public void BatchWithoutDiagnosticsStillListsItsTasksTest()
    {
        var batch = new VerifyResultBatchData
        {
            Results = [new VerifyResultData { TaskIndex = 7 }]
        };

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([batch]));
        var batches = document.RootElement.GetProperty("batches");

        Assert.Multiple(() =>
        {
            Assert.That(batches.GetArrayLength(), Is.EqualTo(1));
            Assert.That(batches[0].GetProperty("tasks")[0].GetInt32(), Is.EqualTo(7));
            Assert.That(batches[0].TryGetProperty("runtime_target", out _), Is.False);
        });
    }

    [Test]
    public void BatchCarriesItsTasksMetricsTest()
    {
        var batch = Batch("python-3.14.6", "aaa", [3, 1]);
        batch.Results[0].FuelConsumed = 300;
        batch.Results[0].PeakMemoryBytes = 65536;
        batch.Results[0].WallMs = 30;
        batch.Results[1].FuelConsumed = 100;
        batch.Results[1].PeakMemoryBytes = 131072;
        batch.Results[1].WallMs = 10;

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([batch]));
        var entry = document.RootElement.GetProperty("batches")[0];
        var metrics = entry.GetProperty("metrics").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(entry.GetProperty("fuel").GetInt64(), Is.EqualTo(400));
            Assert.That(entry.GetProperty("tasks").EnumerateArray().Select(t => t.GetInt32()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(metrics.Select(m => m.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(metrics[0].GetProperty("fuel").GetInt64(), Is.EqualTo(100));
            Assert.That(metrics[0].GetProperty("peak_memory").GetInt64(), Is.EqualTo(131072));
            Assert.That(metrics[0].GetProperty("wall_ms").GetInt32(), Is.EqualTo(10));
            Assert.That(metrics[1].GetProperty("fuel").GetInt64(), Is.EqualTo(300));
        });
    }

    private static VerifyResultBatchData Batch(string runtimeId, string sha256, int[] tasks)
    {
        return new VerifyResultBatchData
        {
            Results = tasks.Select(index => new VerifyResultData { TaskIndex = index, Verdict = VerifyVerdict.Pass }).ToList(),
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                RuntimeTarget = "linux-x64",
                WasmtimeVersion = "48.0.2.0",
                RuntimeId = runtimeId,
                RuntimeSha256 = sha256,
                Degree = 2,
                TaskCount = tasks.Length,
                WallMs = 12,
                ControllerVersion = "0.2.0-test"
            }
        };
    }
}
