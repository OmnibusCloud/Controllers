using System.Text;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// What one shard's PDFs may cost a node besides their fuel. A body that only starts as a PDF
/// costs a run (a folder, two files, an instance of the module, its teardown: some 7 ms) and 1.5
/// million fuel, so the shard's fuel let five million of them through: about ten hours of a core
/// where the documents promised a quarter of an hour. A shard's PDFs now have a number of runs
/// as well as a fuel together.
/// </summary>
[TestFixture]
public sealed class ShardPdfStageRunsTests
{
    #region Fields

    private string m_work = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_work = Path.Combine(Path.GetTempPath(), $"witai_corpus_pdfruns_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_work);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_work);
    }

    #endregion

    #region Tests

    [Test]
    public void AShardOfBodiesThatOnlyStartAsAPdfStopsAtItsRunsTest()
    {
        // The audit's body: 41 bytes that begin as a PDF and are none. Each run drops it as
        // unreadable for very little fuel; past the shard's runs the rest are not run at all.
        var module = PdfTestModule.Require();
        using var sandbox = new PdfSandbox(module, PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES);
        var body = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 32));
        var shard = WarcSynth.Shard(Enumerable.Range(0, 10).Select(index => WarcSynth.PdfResponseRecord($"https://a.example/{index}.pdf", body)));
        var pipeline = new CorpusPipelineData { Version = "runs", IdentifyLanguage = false };

        var product = new ShardProcessor(pipeline, null, new ShardPdfStage(sandbox, null, m_work) { ShardRuns = 4 })
            .Process(new MemoryStream(shard), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Length.EqualTo(41));
            Assert.That(stats.Pdf!.In, Is.EqualTo(10), "every PDF is counted");
            Assert.That(product.Stats.PdfRuns, Is.EqualTo(4), "four were handed to the sandbox");
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EquivalentTo(new[] { ("pdf_unreadable", 4), ("pdf_shard_runs", 6) }), "the other six were not run");
            Assert.That(stats.Pdf.Fuel, Is.LessThan(PdfStageLimits.FUEL), "a run of such a body costs next to no fuel: the fuel would never have stopped them");
        });
    }

    [Test]
    public void TheRunsOfAShardAreBoundedAndPartOfTheStageSetTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfStageLimits.SHARD_RUNS, Is.EqualTo(4_096));
            Assert.That(PdfStageLimits.DESCRIPTOR, Does.Contain($"shard_runs={PdfStageLimits.SHARD_RUNS}"), "a part of the stage set's fingerprint");
            Assert.That(ShardPdfStage.DROPS, Does.Contain(ShardPdfStage.DROP_PDF_SHARD_RUNS));
            Assert.That(ShardDropRules.StageOf(ShardPdfStage.DROP_PDF_SHARD_RUNS), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
        });
    }

    #endregion
}
