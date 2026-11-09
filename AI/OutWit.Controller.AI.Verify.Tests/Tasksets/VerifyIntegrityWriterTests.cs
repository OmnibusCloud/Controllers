using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The integrity document: executions paired by task index, agreement on the identity fields, a
/// task executed once making no pair, and no machine named.
/// </summary>
[TestFixture]
public sealed class VerifyIntegrityWriterTests
{
    [Test]
    public void AgreeingPairsMatchTest()
    {
        var executions = new[]
        {
            Batch(Result(0, VerifyVerdict.Pass, "4\n"), Result(1, VerifyVerdict.Fail, "x\n")),
            Batch(Result(0, VerifyVerdict.Pass, "4\n"), Result(1, VerifyVerdict.Fail, "x\n"))
        };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(executions));
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(pairs.All(p => p.GetProperty("match").GetBoolean()), Is.True);
        });
    }

    [Test]
    public void DisagreementNamesTheFieldsTest()
    {
        var executions = new[]
        {
            Batch(Result(5, VerifyVerdict.Fail, "5\n", exit: 1)),
            Batch(Result(5, VerifyVerdict.Pass, "4\n"))
        };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(executions));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("differs").EnumerateArray().Select(f => f.GetString()), Is.EqualTo(new[] { "verdict", "exit", "stdout" }));
            Assert.That(pair.GetProperty("primary").GetProperty("verdict").GetString(), Is.EqualTo("Pass"), "the rule ranks the lowest verdict first, whatever arrived first");
        });
    }

    [Test]
    public void TheDocumentNamesNoMachineTest()
    {
        var first = Batch(Result(0, VerifyVerdict.Pass, "4\n"));
        var second = Batch(Result(0, VerifyVerdict.Pass, "4\n"));
        first.Diagnostics!.NodeId = Guid.NewGuid();
        first.Diagnostics.NodeName = "contributors-laptop";

        var json = Encoding.UTF8.GetString(VerifyIntegrityWriter.Write([first, second]));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain(first.Diagnostics.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("node"));
        });
    }

    [Test]
    public void ATaskExecutedOnceMakesNoPairTest()
    {
        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write([Batch(Result(0, VerifyVerdict.Pass, ""), Result(1, VerifyVerdict.Pass, ""))]));

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("pairs").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public void ATaskExecutedThreeTimesMakesTwoPairsTest()
    {
        var executions = new[]
        {
            Batch(Result(0, VerifyVerdict.Pass, "4\n")),
            Batch(Result(0, VerifyVerdict.Pass, "4\n")),
            Batch(Result(0, VerifyVerdict.Pass, "5\n"))
        };

        using var document = JsonDocument.Parse(VerifyIntegrityWriter.Write(executions));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
        });
    }

    private static VerifyResultBatchData Batch(params VerifyResultData[] results)
    {
        return new VerifyResultBatchData
        {
            Results = results.ToList(),
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                RuntimeTarget = "linux-x64",
                WasmtimeVersion = "44.0.0.0",
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
