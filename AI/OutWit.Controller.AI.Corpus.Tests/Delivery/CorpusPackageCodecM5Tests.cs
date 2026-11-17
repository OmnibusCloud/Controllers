using System.Buffers.Binary;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Delivery;

/// <summary>
/// Audit finding M-5, at the package format: tasks and results travel as blobs, and a blob a node
/// wrote is read within bounds — a frame larger than a result may be, more frames than the
/// package had shards, a head that claims bytes the stream does not hold, a stream that is no
/// stream each end the reading at a bound, not at the size the bytes ask for.
/// </summary>
[TestFixture]
public sealed class CorpusPackageCodecM5Tests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_codec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Tasks

    [Test]
    public void M5ATaskPackageCarriesThePipelineOnceAndEveryTaskWholeTest()
    {
        var pipeline = CorpusTestRun.Labelled();
        pipeline.TargetLanguages = ["eng", "deu"];
        var tasks = CorpusTestRun.Tasks(5, pipeline);
        tasks[3].DeclaredBytes = 4242;
        tasks[3].Source!.SizeBytes = 4242;

        var path = Write(stream => CorpusPackageCodec.WriteTasks(stream, tasks[0], tasks));
        var back = CorpusPackageCodec.ReadTasks(path, 5);

        Assert.Multiple(() =>
        {
            Assert.That(back, Has.Count.EqualTo(5));
            for (var index = 0; index < tasks.Count; index++)
                Assert.That(back[index].Is(tasks[index]), Is.True, $"task {index} is the task that was written");

            Assert.That(back.Select(task => task.Pipeline), Is.All.SameAs(back[0].Pipeline), "one pipeline for the package, not one per task");
            Assert.That(back.Select(task => task.AllowedOrigins), Is.All.SameAs(back[0].AllowedOrigins));
        });
    }

    [Test]
    public void M5ATaskCostsAFractionOfWhatItCostAsAParameterTest()
    {
        // The audit measured 509 bytes for a typical task as a parameter of its own: the pipeline
        // and the origins repeated in each. In a package they are written once.
        var pipeline = new CorpusPipelineData { Version = "cc-text-v1", LanguageModelBlobId = Guid.NewGuid(), LanguageModelSha256 = CorpusTestRun.Hash('1'), TargetLanguages = ["eng", "deu", "fra", "spa"] };
        var tasks = Enumerable.Range(0, 1000).Select(index => new CorpusTaskData
        {
            TaskIndex = index,
            Source = new CorpusSourceData { Url = $"https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/1791170053823.21/wet/CC-MAIN-20261020000000-20261020030000-{index:D5}.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = 64_000_000 + index },
            Pipeline = pipeline,
            AllowedOrigins = ["data.commoncrawl.org"],
            MaxShardBytes = 1L << 30,
            Ticket = OutWit.Controller.AI.Corpus.Trust.CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION,
            DeclaredBytes = 64_000_000 + index
        }).ToList();

        var asParameters = tasks.Sum(task => (long)MemoryPackSerializer.Serialize(task).Length);
        var asPackage = new FileInfo(Write(stream => CorpusPackageCodec.WriteTasks(stream, tasks[0], tasks))).Length;

        TestContext.Out.WriteLine($"1000 tasks: {asParameters} bytes as parameters, {asPackage} bytes as a package ({(double)asPackage / tasks.Count:F1} bytes a task)");
        Assert.That(asPackage, Is.LessThan(asParameters / 4));
    }

    [Test]
    public void M5ATaskPackageOfMoreTasksThanItsReferenceNamesIsRefusedTest()
    {
        var tasks = CorpusTestRun.Tasks(6);
        var path = Write(stream => CorpusPackageCodec.WriteTasks(stream, tasks[0], tasks));

        Assert.Throws<InvalidDataException>(() => CorpusPackageCodec.ReadTasks(path, 5));
    }

    [Test]
    public void M5AResultPackageIsNoTaskPackageTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var path = Write(stream => CorpusPackageCodec.WriteResults(stream, [CorpusTestRun.Completed(tasks[0])]));

        Assert.Throws<InvalidDataException>(() => CorpusPackageCodec.ReadTasks(path, 10));
    }

    #endregion

    #region Results

    [Test]
    public void M5AResultPackageGivesEveryResultBackTest()
    {
        var tasks = CorpusTestRun.Tasks(3, CorpusTestRun.Signed());
        CorpusResultData[] results =
        [
            CorpusTestRun.Completed(tasks[0], drops: [("min_words", 2), ("language_unsupported", 1)]),
            CorpusTestRun.Fault(tasks[1]),
            CorpusTestRun.Verdict(tasks[2], CorpusShardVerdict.FetchFailed, "http_404")
        ];

        var path = Write(stream => CorpusPackageCodec.WriteResults(stream, results));
        var back = CorpusPackageCodec.ReadResults(path, 3).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(back, Has.Count.EqualTo(3));
            for (var index = 0; index < results.Length; index++)
                Assert.That(back[index]!.Is(results[index]), Is.True, $"result {index}");
        });
    }

    [Test]
    public void M5MoreResultsThanThePackageHadShardsEndTheReadingTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var path = Write(stream => CorpusPackageCodec.WriteResults(stream, Enumerable.Repeat(CorpusTestRun.Completed(tasks[0]), 50)));

        var read = new List<CorpusResultData?>();
        Assert.Throws<InvalidDataException>(() =>
        {
            foreach (var result in CorpusPackageCodec.ReadResults(path, 3))
                read.Add(result);
        });

        Assert.That(read, Has.Count.EqualTo(3), "what was read before the bound stands; nothing past it is read");
    }

    [Test]
    public void M5AFrameLargerThanAResultMayBeEndsTheReadingBeforeItIsReadTest()
    {
        // A head that claims 2 GiB: nothing is allocated for bytes the head only states.
        var path = Frames((CorpusPackageCodec.FRAME_RESULT, int.MaxValue, []));
        var before = GC.GetTotalAllocatedBytes(precise: true);

        Assert.Throws<InvalidDataException>(() => CorpusPackageCodec.ReadResults(path, 10).ToList());
        Assert.That(GC.GetTotalAllocatedBytes(precise: true) - before, Is.LessThan(8 << 20), "refused by its length, not read to find out");
    }

    [Test]
    public void M5AFrameThatClaimsMoreThanTheStreamHoldsCostsNothingTest()
    {
        // Within the cap, but the bytes are not there: the buffer grows with what arrives.
        var path = Frames((CorpusPackageCodec.FRAME_RESULT, CorpusPackageCodec.MAX_RESULT_FRAME_BYTES, new byte[10]));

        Assert.Throws<InvalidDataException>(() => CorpusPackageCodec.ReadResults(path, 10).ToList());
    }

    [Test]
    public void M5AFrameThatIsNoResultIsGivenAsNoneAndTheReadingGoesOnTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var honest = MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[0]));
        var path = Frames(
            (CorpusPackageCodec.FRAME_RESULT, 5, [0xFF, 0xFE, 0xFD, 0xFC, 0xFB]),
            (CorpusPackageCodec.FRAME_RESULT, honest.Length, honest));

        var back = CorpusPackageCodec.ReadResults(path, 2).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(back[0], Is.Null);
            Assert.That(back[1], Is.Not.Null);
        });
    }

    [Test]
    public void M5AListThatClaimsAHundredMillionEntriesInAFewBytesIsNoResultTest()
    {
        // A decode never allocates by a number it was sent: a statistics list that states a
        // length its frame cannot hold is refused by the decoder, within the frame's size.
        var tasks = CorpusTestRun.Tasks(1);
        var result = CorpusTestRun.Completed(tasks[0], drops: [("min_words", 1)]);
        var image = MemoryPackSerializer.Serialize(result);
        var forged = Forge(image, result);
        var path = Frames((CorpusPackageCodec.FRAME_RESULT, forged.Length, forged));
        var before = GC.GetTotalAllocatedBytes(precise: true);

        var back = CorpusPackageCodec.ReadResults(path, 1).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(back.Single(), Is.Null, "no image of a result");
            Assert.That(GC.GetTotalAllocatedBytes(precise: true) - before, Is.LessThan(64 << 20), "nothing was allocated for the entries the header only states");
        });
    }

    [TestCase(new byte[0])]
    [TestCase(new byte[] { 1, 2, 3 })]
    [TestCase(new byte[] { (byte)'W', (byte)'C', (byte)'P', (byte)'K', 2, 9, 0, 0 })]
    [TestCase(new byte[] { (byte)'W', (byte)'C', (byte)'P', (byte)'K', 2, 1, 0, 0, 0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF })]
    public void M5BytesThatAreNoPackageAreRefusedTest(byte[] bytes)
    {
        var path = Path.Combine(m_directory, $"{Guid.NewGuid():N}.wcp");
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => CorpusPackageCodec.ReadResults(path, 10).ToList());
    }

    [Test]
    public void M5TheBlobOfAnAnswerHasASizeItCannotPassTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusPackageCodec.MaxResultsBlobBytes(1), Is.GreaterThan(CorpusPackageCodec.MAX_RESULT_FRAME_BYTES));
            Assert.That(CorpusPackageCodec.MaxResultsBlobBytes(CorpusPackagePlanner.MAX_PACKAGE_SHARDS), Is.LessThan(1L << 28), "the largest answer a package can have is a bounded file");
        });
    }

    #endregion

    #region Tools

    private string Write(Action<Stream> write)
    {
        var path = Path.Combine(m_directory, $"{Guid.NewGuid():N}.wcp");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        write(file);
        return path;
    }

    /// <summary>A result package made of the given frames, each with the length its head states and the bytes it really has.</summary>
    private string Frames(params (byte Kind, int StatedLength, byte[] Bytes)[] frames)
    {
        return Write(stream =>
        {
            stream.Write("WCPK"u8);
            stream.Write([CorpusPackageCodec.KIND_RESULTS, CorpusPackageCodec.VERSION, 0, 0]);

            using var brotli = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true);
            Span<byte> head = stackalloc byte[CorpusPackageCodec.FRAME_HEAD_LENGTH];
            foreach (var (kind, length, bytes) in frames)
            {
                head[0] = kind;
                BinaryPrimitives.WriteInt32LittleEndian(head[1..], length);
                brotli.Write(head);
                brotli.Write(bytes);
            }
        });
    }

    /// <summary>The image of a result whose first list of counts states a length of a hundred million.</summary>
    private static byte[] Forge(byte[] image, CorpusResultData result)
    {
        // The drops list is the first list of the statistics: its header is the count 1 (little
        // endian) right before the one entry's name, "min_words".
        var name = System.Text.Encoding.UTF8.GetBytes("min_words");
        var at = image.AsSpan().IndexOf(name);
        Assert.That(at, Is.GreaterThan(16), "the image holds the name");

        // Walk back to the list's header: the four bytes of 1 that precede the entry.
        var forged = (byte[])image.Clone();
        for (var index = at - 4; index >= 4; index--)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(forged.AsSpan(index, 4)) == 1 && result.Stats!.Drops.Count == 1)
            {
                BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(index, 4), 100_000_000);
                return forged;
            }
        }

        Assert.Fail("the list header was not found in the image");
        return forged;
    }

    #endregion
}
