using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// The reading of an answer on the server: whatever goes wrong while a result package is read
/// costs that package (the reason <c>unreadable</c>) and never the run. A frame whose string
/// states a length outside the frame, an answer whose file will not open, text that is no
/// Unicode in any string of a result: the ledger is built, both documents are written, and the
/// shards the answer left without a result are sent out once more.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusRoundIntakeTests
{
    #region Fields

    private string m_directory = null!;
    private CorpusTestBlobService m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_intake_{Guid.NewGuid():N}");
        m_blobs = new CorpusTestBlobService(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Tests

    [Test]
    public async Task AFrameThatWouldLeadTheDecoderOutsideItCostsThatResultNotTheRunTest()
    {
        // One package of three shards. The node's answer holds three frames; in the first, the
        // content hash states a UTF-8 length of two gigabytes.
        var tasks = CorpusTestRun.Tasks(3);
        var packages = Packages(tasks, perPackage: 3);
        var image = CorpusTestImage.Of(CorpusTestRun.Completed(tasks[0]));
        var bent = image.With(image.Heads.First(head => head.Name == "content_sha256"), int.MinValue);
        var answer = await AnswerAsync(packages[0], bent, MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[1])), MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[2])));

        var ledger = await LedgerAsync(packages, [answer]);
        var documents = Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger)) + Encoding.UTF8.GetString(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }));
            Assert.That(ledger.Shards.Select(shard => shard.Outcome), Is.EqualTo(new[] { CorpusShardOutcome.NoResult, CorpusShardOutcome.Accepted, CorpusShardOutcome.Accepted }));
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 0 }), "the shard of the frame is sent out once more");
            Assert.That(documents, Does.Contain("\"unreadable\": 1"));
        });
    }

    [Test]
    public async Task AnAnswerWhoseFileWillNotOpenCostsThatAnswerNotTheRunTest()
    {
        // Two packages of two shards, both answered. While the run is read, the file of the
        // second answer is held by somebody else: the reading of it fails with the file
        // system's own exception, which is none of the codec's.
        var tasks = CorpusTestRun.Tasks(4);
        var packages = Packages(tasks, perPackage: 2);
        var first = CorpusTestPackages.Answer(m_blobs, packages[0].Ticket, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1])]);
        var second = CorpusTestPackages.Answer(m_blobs, packages[1].Ticket, [CorpusTestRun.Completed(tasks[2]), CorpusTestRun.Completed(tasks[3])]);
        var source = await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, packages, [first, second], m_blobs);

        CorpusLedger? ledger = null;
        byte[] report = [];
        using (new FileStream(await m_blobs.GetLocalPathAsync(second.ResultsBlobId), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.DoesNotThrow(() =>
            {
                ledger = CorpusLedger.Build(CorpusTestRun.VERSION, source);
                report = CorpusReportWriter.Write(ledger);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(report, Is.Not.Empty, "the report is written");
            Assert.That(ledger!.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }), "the answer that could not be read, once");
            Assert.That(ledger.PendingRepairs, Is.EqualTo(2), "its two shards are sent out once more");
        });
    }

    [Test]
    public async Task TextThatIsNoUnicodeInAResultCostsThatResultNotTheRunTest()
    {
        // The wire's UTF-16 form carries any sixteen bits: half a surrogate pair in each string
        // of a result in turn. None of them is taken, and nothing is thrown on the way to the
        // documents.
        var half = "ab" + (char)0xD800 + "cd";
        (string Name, Action<CorpusResultData> Bend)[] bent =
        [
            ("the content hash", result => result.ContentSha256 = half),
            ("the error", result => result.Error = half),
            ("the node name", result => result.NodeName = half),
            ("the controller version", result => result.ControllerVersion = half),
            ("the source URL", result => result.SourceUrl += half),
            ("the runtime target", result => result.RuntimeTarget = half),
            ("the signatures hash", result => result.SignaturesSha256 = half),
            ("the ticket", result => result.Ticket = half),
            ("a drop's name", result => result.Stats!.Drops = [new CorpusCountData { Name = half, Count = 1 }]),
            ("a language's name", result => result.Stats!.Languages = [new CorpusCountData { Name = half, Count = 5 }])
        ];

        var tasks = CorpusTestRun.Tasks(bent.Length + 1);
        var packages = Packages(tasks, perPackage: tasks.Count);
        var frames = new List<byte[]>();
        for (var index = 0; index < bent.Length; index++)
        {
            var result = CorpusTestRun.Completed(tasks[index]);
            bent[index].Bend(result);
            frames.Add(MemoryPackSerializer.Serialize(result, MemoryPackSerializerOptions.Utf16));
        }

        frames.Add(MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[^1])));
        var answer = await AnswerAsync(packages[0], [.. frames]);

        CorpusLedger? ledger = null;
        var documents = "";
        Assert.DoesNotThrow(() =>
        {
            ledger = CorpusLedger.Build(CorpusTestRun.VERSION, CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, packages, [answer], m_blobs).GetAwaiter().GetResult());
            documents = Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger)) + Encoding.UTF8.GetString(CorpusIntegrityWriter.Write(ledger));
        });

        Assert.Multiple(() =>
        {
            Assert.That(ledger!.RejectedResults, Is.EqualTo(bent.Length), "each bent result is dropped, for a reason of the fixed list");
            Assert.That(ledger.Shards.Count(shard => shard.Outcome == CorpusShardOutcome.Accepted), Is.EqualTo(1), "the honest result stands");
            Assert.That(ledger.Shards.Last().Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(documents, Does.Not.Contain("\\uD800").And.Not.Contain("\\ud800").And.Not.Contain("\\uFFFD").And.Not.Contain("\\ufffd").And.Not.Contain(((char)0xFFFD).ToString()), "nothing of the bent strings is in a document, escaped or replaced");
        });
    }

    #endregion

    #region Tools

    /// <summary>Task packages of several shards each, as a job past 1,024 shards has them.</summary>
    private List<CorpusTaskPackageData> Packages(IReadOnlyList<CorpusTaskData> tasks, int perPackage)
    {
        return CorpusTaskPackages.WriteAsync(m_blobs, tasks, perPackage * CorpusPackagePlanner.MAX_PACKAGES).GetAwaiter().GetResult();
    }

    /// <summary>An answer made of the given frames, each an image as it stands: what a node that writes its own bytes sends.</summary>
    private async Task<CorpusResultPackageData> AnswerAsync(CorpusTaskPackageData package, params byte[][] images)
    {
        using var buffer = new MemoryStream();
        buffer.Write("WCPK"u8);
        buffer.Write([CorpusPackageCodec.KIND_RESULTS, CorpusPackageCodec.VERSION, 0, 0]);

        using (var brotli = new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            var head = new byte[CorpusPackageCodec.FRAME_HEAD_LENGTH];
            foreach (var image in images)
            {
                head[0] = CorpusPackageCodec.FRAME_RESULT;
                BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(1), image.Length);
                brotli.Write(head);
                brotli.Write(image);
            }
        }

        return new CorpusResultPackageData { Ticket = package.Ticket, ResultsBlobId = await m_blobs.UploadBytesAsync(buffer.ToArray(), CorpusPackageCodec.RESULTS_FILE_NAME) };
    }

    private async Task<CorpusLedger> LedgerAsync(IEnumerable<object?> packages, IEnumerable<object?> answers)
    {
        return CorpusLedger.Build(CorpusTestRun.VERSION, await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, packages, answers, m_blobs));
    }

    #endregion
}
