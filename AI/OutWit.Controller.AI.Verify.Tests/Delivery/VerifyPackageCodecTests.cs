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
    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_verify_codec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_folder))
            Directory.Delete(m_folder, recursive: true);
    }

    #endregion

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
    public void ResultPackageRoundTripsAsAStreamTest()
    {
        var batches = new List<VerifyResultBatchData>
        {
            new()
            {
                Ticket = new string('a', 32),
                Results =
                [
                    new VerifyResultData { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "4\n" },
                    new VerifyResultData { TaskIndex = 1, Verdict = VerifyVerdict.Fail, ExitCode = 1, Stderr = "boom" }
                ],
                Diagnostics = new VerifyBatchDiagnosticsData { RuntimeId = "python-3.14.6", Degree = 4, WallMs = 12 }
            },
            new()
            {
                Ticket = new string('a', 32),
                Results = [new VerifyResultData { TaskIndex = 2, Verdict = VerifyVerdict.NoResult, ExitCode = -1, Fault = VerifyFault.WALL_CLOCK }],
                Diagnostics = new VerifyBatchDiagnosticsData { RuntimeId = "quickjs-0.15.1" }
            }
        };

        var path = WriteResults(batches);
        var restored = VerifyResultPackageReader.Read(path, 2, 3).ToList();

        Assert.That(restored, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(restored[0].Is(batches[0]), Is.True);
            Assert.That(restored[1].Is(batches[1]), Is.True);
        });
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void AResultPackageIsNotReadAsTasksTest()
    {
        var results = File.ReadAllBytes(WriteResults([new VerifyResultBatchData()]));

        Assert.That(() => VerifyPackageCodec.ReadTasks(results), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ATaskPackageIsNotReadAsResultsTest()
    {
        var path = Path.Combine(m_folder, "tasks.wvp");
        File.WriteAllBytes(path, VerifyPackageCodec.WriteTasks([TaskBatch("python-3.14.6", 0, 1)]));

        Assert.That(() => VerifyResultPackageReader.Read(path, 1, 1).ToList(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void H1AResultPackageWithMoreThanItsTaskPackageHadIsRefusedTest()
    {
        var path = WriteResults(
        [
            new VerifyResultBatchData { Results = [new VerifyResultData { TaskIndex = 0 }, new VerifyResultData { TaskIndex = 1 }] },
            new VerifyResultBatchData { Results = [new VerifyResultData { TaskIndex = 2 }] }
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(() => VerifyResultPackageReader.Read(path, 2, 3).ToList(), Throws.Nothing);
            Assert.That(() => VerifyResultPackageReader.Read(path, 1, 3).ToList(), Throws.TypeOf<VerifyPackageContentException>());
            Assert.That(() => VerifyResultPackageReader.Read(path, 2, 2).ToList(), Throws.TypeOf<VerifyPackageContentException>());
        });
    }

    [Test]
    public void H1AFrameThatClaimsMoreThanAFrameMayHoldIsRefusedWithoutBeingReadTest()
    {
        // A frame head that states two gigabytes, and one that states 60 MiB the stream does not have.
        var huge = Frames((VerifyResultPackageWriter.FRAME_RESULT, int.MaxValue, 16));
        var absent = Frames((VerifyResultPackageWriter.FRAME_BATCH, 60 << 20, 16));

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        Assert.Multiple(() =>
        {
            Assert.That(() => VerifyResultPackageReader.Read(huge, 1, 1).ToList(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("larger than a frame may be"));
            Assert.That(() => VerifyResultPackageReader.Read(absent, 1, 1).ToList(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("ends inside a frame"));
        });

        Assert.That(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore, Is.LessThan(8L << 20), "nothing is allocated by a length that was only stated");
    }

    [Test]
    public void H1ATaskPackageThatUnpacksToMoreThanOneMayHoldIsRefusedTest()
    {
        using var stream = new MemoryStream();
        VerifyPackageCodec.WriteHeader(stream, VerifyPackageCodec.KIND_TASKS, VerifyPackageCodec.TASKS_VERSION);
        using (var brotli = new System.IO.Compression.BrotliStream(stream, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var zeros = new byte[1 << 20];
            for (long written = 0; written <= VerifyPackageCodec.MAX_TASKS_IMAGE_BYTES; written += zeros.Length)
                brotli.Write(zeros);
        }

        Assert.That(() => VerifyPackageCodec.ReadTasks(stream.ToArray()), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unpacks to more"));
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
        Assert.That(() => VerifyPackageCodec.EnsureHash(bytes, "", "task package"), Throws.TypeOf<InvalidDataException>(), "a reference without a hash names no bytes");
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

    private string WriteResults(IReadOnlyList<VerifyResultBatchData> batches)
    {
        var path = Path.Combine(m_folder, $"{Guid.NewGuid():N}.wvp");
        using var writer = new VerifyResultPackageWriter(path);
        foreach (var batch in batches)
            writer.Write(batch);

        writer.Complete();
        return path;
    }

    /// <summary>A result package of frames whose heads state a length and whose bodies are a number of zero bytes.</summary>
    private string Frames(params (byte Kind, int StatedLength, int ActualBytes)[] frames)
    {
        var path = Path.Combine(m_folder, $"{Guid.NewGuid():N}.wvp");
        using var file = File.Create(path);
        VerifyPackageCodec.WriteHeader(file, VerifyPackageCodec.KIND_RESULTS, VerifyPackageCodec.RESULTS_VERSION);
        using var brotli = new System.IO.Compression.BrotliStream(file, System.IO.Compression.CompressionLevel.Fastest);
        foreach (var (kind, stated, actual) in frames)
        {
            brotli.WriteByte(kind);
            brotli.Write(BitConverter.GetBytes(stated));
            brotli.Write(new byte[actual]);
        }

        return path;
    }

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
