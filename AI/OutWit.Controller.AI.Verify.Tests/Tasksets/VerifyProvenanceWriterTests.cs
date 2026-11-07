using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The provenance document: batches ordered by first task, nodes aggregated with their
/// runtime hashes, and no dependence on the order the batches came back in.
/// </summary>
[TestFixture]
public sealed class VerifyProvenanceWriterTests
{
    [Test]
    public void BatchesAreOrderedByFirstTaskAndNodesAggregatedTest()
    {
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();

        var late = Batch(nodeA, "alpha", "python-3.14.6", "aaa", [4, 5]);
        var early = Batch(nodeB, "beta", "quickjs-0.15.1", "bbb", [0, 1, 2]);
        var middle = Batch(nodeA, "alpha", "quickjs-0.15.1", "bbb", [3]);

        using var document = JsonDocument.Parse(VerifyProvenanceWriter.Write([late, early, middle]));
        var root = document.RootElement;

        var batchFirsts = root.GetProperty("batches").EnumerateArray()
            .Select(batch => batch.GetProperty("tasks")[0].GetInt32())
            .ToList();
        Assert.That(batchFirsts, Is.EqualTo(new[] { 0, 3, 4 }));

        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        Assert.That(nodes.Select(node => node.GetProperty("node_name").GetString()), Is.EqualTo(new[] { "alpha", "beta" }));

        var alpha = nodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(alpha.GetProperty("node_id").GetGuid(), Is.EqualTo(nodeA));
            Assert.That(alpha.GetProperty("batches").GetInt32(), Is.EqualTo(2));
            Assert.That(alpha.GetProperty("tasks").GetInt32(), Is.EqualTo(3));
            Assert.That(alpha.GetProperty("runtimes").GetProperty("python-3.14.6").GetString(), Is.EqualTo("aaa"));
            Assert.That(alpha.GetProperty("runtimes").GetProperty("quickjs-0.15.1").GetString(), Is.EqualTo("bbb"));
            Assert.That(alpha.GetProperty("controller").GetString(), Is.EqualTo("0.2.0-test"));
        });
    }

    [Test]
    public void OrderOfArrivalDoesNotChangeTheDocumentTest()
    {
        var node = Guid.NewGuid();
        var first = Batch(node, "n", "python-3.14.6", "aaa", [0, 1]);
        var second = Batch(node, "n", "python-3.14.6", "aaa", [2, 3]);

        var one = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([first, second]));
        var two = Encoding.UTF8.GetString(VerifyProvenanceWriter.Write([second, first]));

        Assert.That(two, Is.EqualTo(one));
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
            Assert.That(document.RootElement.GetProperty("nodes").GetArrayLength(), Is.Zero);
            Assert.That(batches.GetArrayLength(), Is.EqualTo(1));
            Assert.That(batches[0].GetProperty("tasks")[0].GetInt32(), Is.EqualTo(7));
            Assert.That(batches[0].TryGetProperty("node_id", out _), Is.False);
        });
    }

    private static VerifyResultBatchData Batch(Guid nodeId, string nodeName, string runtimeId, string sha256, int[] tasks)
    {
        return new VerifyResultBatchData
        {
            Results = tasks.Select(index => new VerifyResultData { TaskIndex = index, Verdict = VerifyVerdict.Pass }).ToList(),
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                NodeId = nodeId,
                NodeName = nodeName,
                RuntimeTarget = "linux-x64",
                WasmtimeVersion = "44.0.0.0",
                NativeLibraryPath = "(load context)",
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
