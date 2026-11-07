using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The integrity document: pairs by index, agreement on the identity fields, same-node pairs
/// counted but not compared, a replica without a primary reported as unpaired.
/// </summary>
[TestFixture]
public sealed class VerifyIntegrityWriterTests
{
    [Test]
    public void AgreeingPairsOnDifferentNodesMatchTest()
    {
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();
        var primaries = new[] { Batch(nodeA, "alpha", Result(0, VerifyVerdict.Pass, "4\n"), Result(1, VerifyVerdict.Fail, "x\n")) };
        var replicas = new[] { Batch(nodeB, "beta", Result(0, VerifyVerdict.Pass, "4\n"), Result(1, VerifyVerdict.Fail, "x\n")) };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(primaries, replicas));
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(pairs.All(p => p.GetProperty("match").GetBoolean()), Is.True);
            Assert.That(pairs[0].GetProperty("primary").GetProperty("node_name").GetString(), Is.EqualTo("alpha"));
            Assert.That(pairs[0].GetProperty("replica").GetProperty("node_name").GetString(), Is.EqualTo("beta"));
        });
    }

    [Test]
    public void DisagreementNamesTheFieldsTest()
    {
        var primaries = new[] { Batch(Guid.NewGuid(), "alpha", Result(5, VerifyVerdict.Pass, "4\n")) };
        var replicas = new[] { Batch(Guid.NewGuid(), "beta", Result(5, VerifyVerdict.Fail, "5\n", exit: 1)) };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(primaries, replicas));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("differs").EnumerateArray().Select(f => f.GetString()), Is.EqualTo(new[] { "verdict", "exit", "stdout" }));
        });
    }

    [Test]
    public void SameNodePairIsCountedNotComparedTest()
    {
        var node = Guid.NewGuid();
        var primaries = new[] { Batch(node, "alpha", Result(0, VerifyVerdict.Pass, "4\n")) };
        var replicas = new[] { Batch(node, "alpha", Result(0, VerifyVerdict.Fail, "wrong\n")) };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(primaries, replicas));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("same_node").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero, "a node cannot catch itself");
            Assert.That(pair.GetProperty("compared").GetBoolean(), Is.False);
            Assert.That(pair.TryGetProperty("match", out _), Is.False);
        });
    }

    [Test]
    public void ReplicaWithoutAPrimaryIsUnpairedAndRateZeroIsEmptyTest()
    {
        var replicas = new[] { Batch(Guid.NewGuid(), "beta", Result(9, VerifyVerdict.Pass, "")) };

        using var unpaired = JsonDocument.Parse(VerifyIntegrityWriter.Write([], replicas));
        using var empty = JsonDocument.Parse(VerifyIntegrityWriter.Write([Batch(Guid.NewGuid(), "alpha", Result(0, VerifyVerdict.Pass, ""))], []));

        Assert.Multiple(() =>
        {
            Assert.That(unpaired.RootElement.GetProperty("summary").GetProperty("unpaired").GetInt32(), Is.EqualTo(1));
            Assert.That(unpaired.RootElement.GetProperty("pairs")[0].GetProperty("primary").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(empty.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
            Assert.That(empty.RootElement.GetProperty("pairs").GetArrayLength(), Is.Zero);
        });
    }

    private static VerifyResultBatchData Batch(Guid nodeId, string nodeName, params VerifyResultData[] results)
    {
        return new VerifyResultBatchData
        {
            Results = results.ToList(),
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                NodeId = nodeId,
                NodeName = nodeName,
                RuntimeTarget = "linux-x64",
                WasmtimeVersion = "44.0.0.0",
                NativeLibraryPath = "(load context)",
                RuntimeId = "python-3.14.6",
                RuntimeSha256 = "aaa",
                Degree = 2,
                TaskCount = results.Length,
                WallMs = 1,
                ControllerVersion = "0.3.1-test"
            }
        };
    }

    private static VerifyResultData Result(int index, VerifyVerdict verdict, string stdout, int exit = 0)
    {
        return new VerifyResultData { TaskIndex = index, Verdict = verdict, ExitCode = exit, Stdout = stdout };
    }
}
