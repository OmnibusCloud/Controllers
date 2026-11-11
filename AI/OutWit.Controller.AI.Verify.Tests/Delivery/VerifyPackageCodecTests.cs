using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// The package blob: what goes in comes out, the two kinds are not confused, a foreign or
/// damaged blob is refused by name, and text-heavy tasks shrink on the way.
/// </summary>
[TestFixture]
public sealed class VerifyPackageCodecTests
{
    #region Round Trip Tests

    [Test]
    public void TaskPackageRoundTripsTest()
    {
        var batches = new List<VerifyTaskBatchData>
        {
            TaskBatch("python-3.14.6", 0, 3),
            TaskBatch("quickjs-0.15.1", 3, 2)
        };

        var restored = VerifyPackageCodec.ReadTasks(VerifyPackageCodec.WriteTasks(batches));

        Assert.That(restored, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(restored[0].Is(batches[0]), Is.True);
            Assert.That(restored[1].Is(batches[1]), Is.True);
        });
    }

    [Test]
    public void ResultPackageRoundTripsTest()
    {
        var batches = new List<VerifyResultBatchData>
        {
            new()
            {
                Results =
                [
                    new VerifyResultData { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "4\n" },
                    new VerifyResultData { TaskIndex = 1, Verdict = VerifyVerdict.Fail, ExitCode = 1, Stderr = "boom" }
                ],
                Diagnostics = new VerifyBatchDiagnosticsData { RuntimeId = "python-3.14.6", Degree = 4, WallMs = 12 }
            }
        };

        var restored = VerifyPackageCodec.ReadResults(VerifyPackageCodec.WriteResults(batches));

        Assert.That(restored, Has.Count.EqualTo(1));
        Assert.That(restored[0].Is(batches[0]), Is.True);
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void AResultPackageIsNotReadAsTasksTest()
    {
        var results = VerifyPackageCodec.WriteResults([new VerifyResultBatchData()]);

        Assert.That(() => VerifyPackageCodec.ReadTasks(results), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void AForeignBlobIsRefusedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => VerifyPackageCodec.ReadTasks(Encoding.UTF8.GetBytes("{\"index\":0}")), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => VerifyPackageCodec.ReadTasks([]), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void AnotherVersionIsRefusedByItsNumberTest()
    {
        var bytes = VerifyPackageCodec.WriteTasks([TaskBatch("python-3.14.6", 0, 1)]);
        bytes[5] = 9;

        Assert.That(() => VerifyPackageCodec.ReadTasks(bytes), Throws.TypeOf<InvalidDataException>().With.Message.Contains("version 9"));
    }

    [Test]
    public void TheHashNamesTheBytesTest()
    {
        var bytes = VerifyPackageCodec.WriteTasks([TaskBatch("python-3.14.6", 0, 2)]);
        var hash = VerifyPackageCodec.Hash(bytes);

        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(() => VerifyPackageCodec.EnsureHash(bytes, hash, "task package"), Throws.Nothing);

        bytes[^1] ^= 0x01;

        Assert.That(() => VerifyPackageCodec.EnsureHash(bytes, hash, "task package"), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => VerifyPackageCodec.EnsureHash(bytes, "", "task package"), Throws.Nothing, "a reference without a hash skips the check");
    }

    #endregion

    #region Size Tests

    [Test]
    public void TextHeavyTasksShrinkTest()
    {
        var batch = TaskBatch("python-3.14.6", 0, 256);
        var raw = batch.Tasks.Sum(task => task.Sources.Sum(source => source.Content.Length));

        var bytes = VerifyPackageCodec.WriteTasks([batch]);

        TestContext.Out.WriteLine($"{raw} characters of source in {bytes.Length} bytes");
        Assert.That(bytes.Length, Is.LessThan(raw / 3));
    }

    #endregion

    #region Tools

    private static VerifyTaskBatchData TaskBatch(string runtime, int firstIndex, int count)
    {
        return new VerifyTaskBatchData
        {
            RuntimeId = runtime,
            MaxDegree = 4,
            DefaultLimits = new VerifyLimitsData(),
            Tasks = Enumerable.Range(firstIndex, count)
                .Select(index => new VerifyTaskData
                {
                    TaskIndex = index,
                    RuntimeId = runtime,
                    EntryPoint = "main.py",
                    Sources = [new VerifySourceFileData { Name = "main.py", Content = $"def solve(a, b):\n    return a + b\n\nprint(solve({index}, {index * 2}))\n" }],
                    Suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedStdout = $"{index * 3}\n" }] },
                    RandomSeed = index
                })
                .ToList()
        };
    }

    #endregion
}
