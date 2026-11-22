using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// A result's counters are held to what the server knows of the shard: the bytes the result says
/// it fetched bound its records, its records bound its documents, a PDF is a record and its
/// counts are the PDF stage's own, and PDF counts appear only where the task runs that stage.
/// The sums the report makes of the counters do not wrap, whatever a run adds up to. What an
/// honest node counts is inside every bound.
/// </summary>
[TestFixture]
public sealed class CorpusResultCountersTests
{
    #region Records and documents

    [Test]
    public void TheSmallestRecordIsTheUnitOfTheBoundTest()
    {
        // A version line, a Content-Length of one digit, the blank line: nothing shorter is a record.
        var lineFeed = (char)10;
        var record = "WARC/" + lineFeed + "Content-Length:0" + lineFeed + lineFeed;
        var bytes = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(record, 1000)));

        using var reader = new WarcReader(new MemoryStream(bytes), maxUnpackedBytes: bytes.Length);
        var records = reader.ReadRecords().Count();

        Assert.Multiple(() =>
        {
            Assert.That(record, Has.Length.EqualTo(ShardLimits.MIN_RECORD_BYTES));
            Assert.That(records, Is.EqualTo(1000), "a file of nothing but the smallest records holds its bytes over their size");
            Assert.That(ShardLimits.MaxRecords(1000), Is.EqualTo(ShardLimits.MIN_UNPACKED_BYTES / ShardLimits.MIN_RECORD_BYTES));
            Assert.That(ShardLimits.MaxRecords(1L << 30), Is.EqualTo(ShardLimits.MAX_UNPACKED_BYTES / ShardLimits.MIN_RECORD_BYTES));
            Assert.That(ShardLimits.MaxRecords(1L << 30), Is.LessThan(int.MaxValue), "the largest shard cannot hold as many records as a counter can state");
        });
    }

    [Test]
    public void RecordsTheFetchedBytesCannotHoldAreRefusedTest()
    {
        // The audit's result: 2,147,483,647 records, documents extracted and documents out, from
        // 1,000 bytes fetched. Its sums agree among themselves, which was all that was asked.
        var result = Counted(int.MaxValue, int.MaxValue);
        var most = (int)ShardLimits.MaxRecords(1000);

        Assert.Multiple(() =>
        {
            Assert.That(result.Stats!.BytesFetched, Is.EqualTo(1000));
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.False, "two thousand million records in a thousand bytes");
            Assert.That(CorpusResultCheck.IsWellFormed(Counted(most, 1)), Is.True, "the most records a shard of this size can unpack to (and documents its product may hold)");
            Assert.That(CorpusResultCheck.IsWellFormed(Counted(most + 1, 1)), Is.False, "one more");
        });
    }

    [Test]
    public void MoreDocumentsThanRecordsAreRefusedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultCheck.IsWellFormed(Counted(10, 10)), Is.True, "a document for every record");
            Assert.That(CorpusResultCheck.IsWellFormed(Counted(10, 11)), Is.False, "a record gives one document at the most");
        });
    }

    [Test]
    public void ACountBeyondTheBoundCostsThatResultNotTheRunTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var forged = Counted(int.MaxValue, int.MaxValue, tasks[1]);

        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), forged]);
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }));
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }), "the shard is sent out once more");
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("documents_out").GetInt64(), Is.EqualTo(5), "the forged count is in no sum");
        });
    }

    #endregion

    #region PDFs

    [Test]
    public void ThePdfCountsAreHeldToOneAnotherTest()
    {
        (string Name, Action<CorpusShardStatsData> Bend)[] bent =
        [
            ("more PDFs than records", stats => stats.Pdf!.In = stats.RecordsIn + 1),
            ("fewer pages than PDFs that gave text", stats => stats.Pdf!.Pages = 1),
            ("fewer documents than PDFs that gave text", stats => stats.Pdf!.Documents = 1),
            ("more documents than pages", stats => stats.Pdf!.Documents = stats.Pdf.Pages + 1),
            ("more pages cut than documents", stats => stats.Pdf!.PagesCut = stats.Pdf.Documents + 1),
            ("more pages than the text of the PDFs can be", stats => stats.Pdf!.Pages = 40_000_000),
            ("more running lines than the text of the PDFs has characters", stats => stats.Pdf!.RunningLines = 40_000_000),
            ("more documents of PDFs than documents", stats => { stats.Pdf!.Pages = 100; stats.Pdf.Documents = stats.DocumentsExtracted + 1; }),
            ("more documents beside the PDFs' than records that are no PDF", stats => { stats.Pdf!.In = 1; stats.Pdf.Truncated = 0; stats.Pdf.Extracted = 1; stats.Pdf.Documents = 1; stats.RecordsIn = stats.DocumentsExtracted - 1; })
        ];

        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultCheck.IsWellFormed(WithPdfs()), Is.True, "the counts a PDF stage makes");
            Assert.That(2 * CorpusResultCheck.MAX_PDF_PAGES, Is.LessThan(40_000_000), "two PDFs cannot have forty million pages");
            Assert.That(2L * PdfStageLimits.MAX_TEXT_BYTES, Is.LessThan(40_000_000));

            foreach (var (name, bend) in bent)
            {
                var result = WithPdfs();
                bend(result.Stats!);

                Assert.That(CorpusResultCheck.IsWellFormed(result), Is.False, name);
            }
        });
    }

    [Test]
    public void PdfCountsWhereTheTaskRunsNoPdfStageAreRejectedTest()
    {
        var off = CorpusTestRun.Plain();
        off.ExtractPdf = false;

        (string Name, CorpusSourceFormat Format, CorpusPipelineData Pipeline, string[] Rejected)[] cases =
        [
            ("a WARC shard with the stage on", CorpusSourceFormat.Warc, CorpusTestRun.Plain(), []),
            ("a WET shard: it holds text, not responses", CorpusSourceFormat.Wet, CorpusTestRun.Plain(), [CorpusLedger.REJECTED_CONTENT]),
            ("a WARC shard with the stage off", CorpusSourceFormat.Warc, off, [CorpusLedger.REJECTED_CONTENT])
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, format, pipeline, rejected) in cases)
            {
                var task = CorpusTestRun.Task(0, pipeline);
                task.Source!.Format = format;

                var ledger = CorpusTestRun.Ledger([task], [WithPdfs(task)]);

                Assert.That(ledger.Rejected.Keys, Is.EqualTo(rejected), name);
            }
        });
    }

    #endregion

    #region Sums

    [Test]
    public void ThePdfSumsOfARunAreNotWrappedTest()
    {
        // Three shards of the most pages a 32-bit count states: the summary's sum was 32 bits
        // too and read 2,147,483,645.
        var tasks = CorpusTestRun.Tasks(3);
        var results = tasks.Select(task =>
        {
            task.Source!.Format = CorpusSourceFormat.Warc;
            var result = CorpusTestRun.Completed(task, documentsOut: 200);
            result.Stats!.Pdf = new CorpusPdfStatsData { In = 200, Extracted = 200, Pages = int.MaxValue, Documents = 200, RunningLines = int.MaxValue, Fuel = PdfStageLimits.SHARD_FUEL };
            return result;
        }).ToList();

        var ledger = CorpusTestRun.Ledger(tasks, results);
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        var summary = report.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(ledger.RejectedResults, Is.Zero, "each is a result the checks take");
            Assert.That(summary.GetProperty("pdf_pages").GetInt64(), Is.EqualTo(3L * int.MaxValue));
            Assert.That(summary.GetProperty("pdf_running_lines").GetInt64(), Is.EqualTo(3L * int.MaxValue));
            Assert.That(summary.GetProperty("pdf_in").GetInt64(), Is.EqualTo(600));
            Assert.That(summary.GetProperty("pdf_fuel").GetInt64(), Is.EqualTo(3 * PdfStageLimits.SHARD_FUEL));
        });
    }

    [Test]
    public void ASumPastTheLargestNumberStopsThereTest()
    {
        // A product's bytes are held to what the bytes fetched may give since 0.9.0: a result that
        // says its product has the largest number of bytes is dropped. The report's sums still stop
        // at the largest number, whatever they are handed: two such numbers must not add up to a
        // negative one.
        var tasks = CorpusTestRun.Tasks(2);
        var results = tasks.Select(task =>
        {
            var result = CorpusTestRun.Completed(task);
            result.Stats!.BytesProduced = long.MaxValue;
            return result;
        }).ToList();

        var ledger = CorpusTestRun.Ledger(tasks, results);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.RejectedResults, Is.EqualTo(2), "more than the bytes fetched may give");
            Assert.That(CorpusReportWriter.Sum(long.MaxValue, long.MaxValue), Is.EqualTo(long.MaxValue));
            Assert.That(CorpusReportWriter.Sum(long.MaxValue - 1, 1), Is.EqualTo(long.MaxValue));
            Assert.That(CorpusReportWriter.Sum(40, 2), Is.EqualTo(42));
        });
    }

    #endregion

    #region Honest counts

    [TestCase(CorpusFixtures.WET, CorpusSourceFormat.Wet)]
    [TestCase(CorpusFixtures.WARC, CorpusSourceFormat.Warc)]
    public void WhatAnHonestNodeCountsIsInsideEveryBoundTest(string fixture, CorpusSourceFormat format)
    {
        // The generated shards through the stages themselves (the PDF stage off: it needs the
        // extractor module, and the bundled script's tests run it through the whole ledger).
        var path = CorpusFixtures.PathOf(fixture);
        var pipeline = new CorpusPipelineData { Version = "test", ExtractPdf = false };
        var task = CorpusTestRun.Task(0, pipeline);
        task.Source!.Format = format;

        using var shard = File.OpenRead(path);
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();
        var product = new ShardProcessor(pipeline, LanguageModel.Load(CorpusFixtures.PathOf(CorpusFixtures.PACK))).Process(shard, format, text, signatures);

        var result = new CorpusResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = CorpusShardVerdict.Completed,
            TextBlobId = Guid.NewGuid(),
            SignaturesBlobId = Guid.NewGuid(),
            ContentSha256 = product.ContentSha256,
            SignaturesSha256 = product.SignaturesSha256,
            MinHash = MinHashParameters.Resolve(pipeline.MinHash).ToData(),
            ControllerVersion = CorpusTestRun.VERSION,
            SourceUrl = task.Source.Url,
            RuntimeTarget = "linux-x64",
            Output = pipeline.Output,
            Ticket = task.Ticket,
            Stats = product.Stats.Build(new FileInfo(path).Length, text.Length, 1, 1)
        };

        var ledger = CorpusTestRun.Ledger([task], [result]);
        TestContext.Out.WriteLine($"{fixture}: {result.Stats.RecordsIn} records of the {ShardLimits.MaxRecords(result.Stats.BytesFetched)} its {result.Stats.BytesFetched} bytes could hold, {result.Stats.DocumentsExtracted} documents extracted, {result.Stats.DocumentsOut} out");

        Assert.Multiple(() =>
        {
            Assert.That(result.Stats.RecordsIn, Is.GreaterThan(100));
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
            Assert.That(ledger.RejectedResults, Is.Zero);
            Assert.That(ledger.Shards.Single().Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
        });
    }

    #endregion

    #region Tools

    /// <summary>A completed result of a shard fetched as 1,000 bytes whose counters agree among themselves: so many records, so many documents extracted and out.</summary>
    private static CorpusResultData Counted(int records, int documents, CorpusTaskData? task = null)
    {
        var result = CorpusTestRun.Completed(task ?? CorpusTestRun.Task(0));
        result.Stats!.RecordsIn = records;
        result.Stats.DocumentsExtracted = documents;
        result.Stats.DocumentsOut = documents;
        result.Stats.Languages = CorpusTestRun.Counts([("", documents)]);

        return result;
    }

    /// <summary>A completed result with the counts a PDF stage makes: three PDFs, two of which gave text.</summary>
    private static CorpusResultData WithPdfs(CorpusTaskData? task = null)
    {
        var result = CorpusTestRun.Completed(task ?? CorpusTestRun.Task(0), documentsOut: 6, drops: [("pdf_encrypted", 1)]);
        result.Stats!.Pdf = new CorpusPdfStatsData { In = 3, Truncated = 1, Extracted = 2, Pages = 14, Documents = 3, PagesCut = 1, RunningLines = 20, Fuel = 1000 };

        return result;
    }

    #endregion
}
