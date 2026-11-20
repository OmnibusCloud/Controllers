using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// What a node knows before it does any work refuses its whole batch, with nothing fetched: the
/// job's controller version (audit finding C-1: one job, one version), the PDF sandbox a WARC
/// shard needs (H-9: found out at the first PDF, with the node's finished shards lost), room on
/// the disk (H-7). What leaves the node is a fixed sentence.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardBatchGuardTests
{
    #region Setup

    [SetUp]
    [TearDown]
    public void Forget()
    {
        ShardBatchGuard.Clear();
        PdfSandboxHost.ClearOverride();
    }

    #endregion

    #region Tests

    [Test]
    public void C1ANodeOnAnotherVersionRefusesItsBatchTest()
    {
        var task = CorpusTestRun.Task(0);
        task.RequiredControllerVersion = "0.3.0";

        var refusal = ShardBatchGuard.Refusal(task, "0.4.0", () => null, () => true);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Is.Not.Null);
            Assert.That(refusal!.Code, Is.EqualTo(ShardBatchRefusedException.VERSION));
            Assert.That(refusal.Message, Is.EqualTo("This node runs Corpus 0.4.0 and the job requires 0.3.0: one job runs on one controller version."));
            Assert.That(ShardBatchGuard.Refusal(CorpusTestRun.Task(0), CorpusTestRun.VERSION, () => null, () => true), Is.Null, "the job's version is this node's");
        });
    }

    [TestCase("")]
    [TestCase("C:\\Users\\someone\\a path")]
    [TestCase("0.4.0\nand a second line")]
    public void C1ATaskWithoutAVersionIsRefusedAndItsTextIsNotRepeatedTest(string required)
    {
        var task = CorpusTestRun.Task(0);
        task.RequiredControllerVersion = required;

        var refusal = ShardBatchGuard.Refusal(task, "0.4.0", () => null, () => true);

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Code, Is.EqualTo(ShardBatchRefusedException.VERSION));
            Assert.That(refusal.Message, Is.EqualTo("This node runs Corpus 0.4.0 and the job requires another version: one job runs on one controller version."));
        });
    }

    [Test]
    public void H9ANodeWithoutTheSandboxRefusesAWarcBatchBeforeItStartsTest()
    {
        var warc = CorpusTestRun.Task(0, new CorpusPipelineData { Version = "test", IdentifyLanguage = false });
        warc.Source!.Format = CorpusSourceFormat.Warc;
        var withoutPdfStage = warc.Clone();
        withoutPdfStage.Pipeline!.ExtractPdf = false;
        var wet = CorpusTestRun.Task(1);
        var asked = 0;

        string? Problem()
        {
            asked++;
            return "the PDF sandbox cannot start: DllNotFoundException at C:\\Users\\someone\\client\\runtimes";
        }

        var refusal = ShardBatchGuard.Refusal(warc, CorpusTestRun.VERSION, Problem, () => true);

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Code, Is.EqualTo(ShardBatchRefusedException.PDF_SANDBOX));
            Assert.That(refusal.Message, Is.EqualTo("This node cannot run the PDF stage and the batch holds a WARC shard that needs it: another node takes the batch."), "the reason stays in the node's log");
            Assert.That(ShardBatchGuard.Refusal(warc, CorpusTestRun.VERSION, () => null, () => true), Is.Null, "a node with the sandbox takes it");
            Assert.That(ShardBatchGuard.Refusal(withoutPdfStage, CorpusTestRun.VERSION, Problem, () => true), Is.Null, "the stage is off: no sandbox is needed");
            Assert.That(ShardBatchGuard.Refusal(wet, CorpusTestRun.VERSION, Problem, () => true), Is.Null, "a WET shard needs none");
            Assert.That(asked, Is.EqualTo(1), "the sandbox is asked for only when a shard needs it");
        });
    }

    [Test]
    public void H7ANodeWithoutRoomForTheShardRefusesItsBatchTest()
    {
        var refusal = ShardBatchGuard.Refusal(CorpusTestRun.Task(0), CorpusTestRun.VERSION, () => null, () => false);

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Code, Is.EqualTo(ShardBatchRefusedException.DISK_SPACE));
            Assert.That(refusal.Message, Is.EqualTo("This node's disk has no room for a shard of the batch: another node takes the batch."));
        });
    }

    [Test]
    public void ARefusalStopsTheRestOfTheJobsBatchOnThisNodeTest()
    {
        var job = Guid.NewGuid();
        var other = Guid.NewGuid();
        var task = CorpusTestRun.Task(0);
        task.RequiredControllerVersion = "0.0.1";
        var work = Path.Combine(Path.GetTempPath(), $"witai_corpus_guard_{Guid.NewGuid():N}");

        Assert.Multiple(() =>
        {
            Assert.That(() => ShardBatchGuard.ThrowIfRefused(job, task, work), Throws.TypeOf<ShardBatchRefusedException>().With.Property("Code").EqualTo(ShardBatchRefusedException.VERSION));
            Assert.That(() => ShardBatchGuard.ThrowIfJobRefused(job), Throws.TypeOf<ShardBatchRefusedException>(), "a task of the same batch that waited for its turn stops before it fetches");
            Assert.That(() => ShardBatchGuard.ThrowIfRefused(job, CorpusTestRun.Task(1), work), Throws.TypeOf<ShardBatchRefusedException>(), "and one that would have been taken goes with the batch");
            Assert.That(() => ShardBatchGuard.ThrowIfJobRefused(other), Throws.Nothing);
            Assert.That(() => ShardBatchGuard.ThrowIfRefused(other, CorpusTestRun.Task(0), work), Throws.Nothing);
            Assert.That(Directory.Exists(work), Is.False, "nothing is made for a refused batch");
        });
    }

    [Test]
    public void M1ANodeThatCannotMakeItsWorkFolderRefusesInFixedWordsTest()
    {
        // Whatever a node throws out of the activity is the job's message for the lab: the file
        // system's own words name the node's folders and its account.
        var storage = new FailingStorage(new UnauthorizedAccessException(@"Access to the path 'C:\Users\someone\temp\corpus-1' is denied."));

        var refusal = Assert.Throws<ShardBatchRefusedException>(() => ShardBatchGuard.CreateScope(storage, "corpus-1"))!;

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Code, Is.EqualTo(ShardBatchRefusedException.DISK));
            Assert.That(refusal.Message, Is.EqualTo("This node cannot make its work folder: another node takes the batch."));
            Assert.That(refusal.InnerException, Is.Null, "nothing of the failure travels with it");
            Assert.That(ShardBatchGuard.CreateScope(new FailingStorage(null), "corpus-1"), Is.EqualTo("made:corpus-1"));
        });
    }

    [Test]
    public void M1ANodeThatFailsWhileItChecksItselfRefusesInFixedWordsTest()
    {
        var task = CorpusTestRun.Task(0);
        task.Source!.Format = CorpusSourceFormat.Warc;
        task.Pipeline!.ExtractPdf = true;

        var sandbox = ShardBatchGuard.Check(task, CorpusTestRun.VERSION, () => throw new TypeLoadException(@"Could not load type 'X' from C:\Users\someone\client\Wasmtime.Dotnet.dll"), () => true)!;
        var disk = ShardBatchGuard.Check(CorpusTestRun.Task(0), CorpusTestRun.VERSION, () => null, () => throw new IOException(@"The device D:\ is not ready."))!;

        Assert.Multiple(() =>
        {
            Assert.That(sandbox.Code, Is.EqualTo(ShardBatchRefusedException.INTERNAL));
            Assert.That(sandbox.Message, Is.EqualTo("This node failed before it could start the batch: another node takes the batch."));
            Assert.That(disk.Code, Is.EqualTo(ShardBatchRefusedException.INTERNAL));
            Assert.That(disk.Message, Is.EqualTo(sandbox.Message));
            Assert.That(ShardBatchGuard.Check(CorpusTestRun.Task(0), CorpusTestRun.VERSION, () => null, () => true), Is.Null, "a node that is well refuses nothing");
            Assert.That(ShardBatchRefusedException.CODES, Is.EqualTo(new[] { "version", "pdf_sandbox", "disk_space", "disk", "internal" }));
        });
    }

    [Test]
    public void ShardDiskNeedsTheShardItsProductsAndAMarginTest()
    {
        var here = Path.Combine(Path.GetTempPath(), $"witai_corpus_disk_{Guid.NewGuid():N}", "not", "made", "yet");
        var free = ShardDisk.FreeBytes(here);

        Assert.Multiple(() =>
        {
            Assert.That(ShardDisk.Needed(1000), Is.EqualTo(1000 + ShardProductLimits.MaxProductBytes(1000, null) + ShardDisk.MARGIN_BYTES), "the shard, the most its products may be, and the margin");
            Assert.That(ShardDisk.NeededByNode(1L << 30, 8), Is.EqualTo(10 * ShardDisk.Needed(1L << 30)), "the degree and two more shards");
            Assert.That(free, Is.GreaterThan(0), "the volume of a folder that does not exist yet is its nearest ancestor's");
            Assert.That(ShardDisk.HasRoom(here, 1), Is.True);
            Assert.That(ShardDisk.HasRoom(here, 1L << 50), Is.False);
            Assert.That(ShardBatchGuard.RoomBytes(new CorpusTaskData { Source = new CorpusSourceData(), MaxShardBytes = 1L << 30 }), Is.EqualTo(1L << 30), "a shard of unknown size is asked for the cap it is held to");
        });
    }

    #endregion

    #region Types

    /// <summary>A host's temp storage that cannot make a scope (or can, for the other half of the test).</summary>
    private sealed class FailingStorage(Exception? failure) : OutWit.Engine.Interfaces.IWitTempStorage
    {
        public string RootPath => "";

        public string CreateScope(string label)
        {
            return failure == null ? $"made:{label}" : throw failure;
        }

        public string CreateTempFile(string? extension = null)
        {
            throw new NotSupportedException();
        }

        public void DeleteScope(string scopePath)
        {
        }

        public void DeleteFile(string path)
        {
        }
    }

    #endregion
}
