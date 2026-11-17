using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Audit finding M-3: a hostile shard had no bound in memory or time, and a record was not
/// cancellable. Half a megabyte of gzip held half a gigabyte of line feeds that the reader walked
/// to the end, a byte at a time, with the job cancelled; one WET record of 16 MiB cost 15 s and
/// 2 GB of allocations. A shard now stops at the most it may unpack to, a record of a size its
/// format never has is skipped and counted, the token is looked at inside a record, and a
/// document of millions of words fails its length rule before it is split into them.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardProcessorM3Tests
{
    #region Tests

    [Test]
    public void M3AShardOfLineFeedsStopsAtWhatAShardMayUnpackToTest()
    {
        // The audit's bomb: 512 MiB of line feeds in 522 KB. It took 46.8 s and gave no record.
        var bomb = Gzip(512L << 20, (byte)'\n');
        var processor = new ShardProcessor(Plain(), null);

        var clock = Stopwatch.StartNew();
        var exception = Assert.Throws<ShardUnpackedOversizedException>(() => processor.Process(new MemoryStream(bomb), CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream()));

        Assert.Multiple(() =>
        {
            Assert.That(bomb.Length, Is.LessThan(1 << 20));
            Assert.That(exception!.Limit, Is.EqualTo(ShardLimits.MIN_UNPACKED_BYTES), "a small file may unpack to 64 MiB, whatever it claims to hold");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)), "an eighth of the bomb is read, not all of it");
            Assert.That(ShardLimits.MaxUnpackedBytes(60L << 20), Is.EqualTo(32 * (60L << 20)), "a Common Crawl WET file unpacks to 3 or 4 times its size: thirty-two is far away");
            Assert.That(ShardLimits.MaxUnpackedBytes(1L << 30), Is.EqualTo(ShardLimits.MAX_UNPACKED_BYTES), "and no shard unpacks past 16 GiB");
        });
    }

    [Test]
    public async Task M3AShardThatUnpacksPastItsCeilingIsOversizedOnEveryNodeTest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"witai_corpus_m3_{Guid.NewGuid():N}");
        var shards = Path.Combine(root, "shards");
        Directory.CreateDirectory(shards);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(shards, "bomb.warc.wet.gz"), Gzip(200L << 20, (byte)'\n'));
            using var origin = new CorpusTestOrigin(shards);
            var task = new CorpusTaskData
            {
                TaskIndex = 0,
                Source = new CorpusSourceData { Url = origin.ShardUrl("bomb.warc.wet.gz"), Format = CorpusSourceFormat.Wet },
                Pipeline = Plain(),
                AllowedOrigins = [origin.Host],
                MaxShardBytes = 1L << 30,
                Ticket = CorpusTickets.New()
            };

            var result = await ShardTaskRunner.RunAsync(task, new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy), new CorpusTestBlobService(Path.Combine(root, "blobs")), Path.Combine(root, "work"), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized), "a verdict on the shard, the same on every node");
                Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED_UNPACKED));
                Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void M3TheTokenIsLookedAtInsideARunOfBlankLinesTest()
    {
        // Within the ceiling the reader still walks millions of blank lines; a cancelled job
        // frees the thread there, not at the next record (there is none).
        var blanks = Gzip(48L << 20, (byte)'\n');
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var clock = Stopwatch.StartNew();
        using var reader = new WarcReader(new MemoryStream(blanks), cancellationToken: cancelled.Token);

        Assert.Multiple(() =>
        {
            Assert.That(() => reader.ReadRecords().ToList(), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(reader.BytesRead, Is.LessThan(1 << 20), "within a few kilobytes of where it was cancelled");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void M3ARecordThatClaimsMoreThanTheShardMayHoldIsRefusedBeforeItIsReadTest()
    {
        // A header is a claim: a record of a stated length past the ceiling ends the shard where
        // the claim is read, the same byte on every node.
        var record = Encoding.ASCII.GetBytes("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length: 999999999999\r\n\r\ntext");
        using var reader = new WarcReader(new MemoryStream(record), maxUnpackedBytes: 1 << 20);

        Assert.That(() => reader.ReadRecords().ToList(), Throws.TypeOf<ShardUnpackedOversizedException>());
    }

    [TestCase(16)]
    [TestCase(64)]
    public void M3AWetRecordNoPageEverHasIsSkippedAndCountedTest(int mebibytes)
    {
        // The audit's record: 16 MiB of short words took 15.6 s and 2,080 MiB of allocations;
        // 64 MiB took 50.9 s and 7,680 MiB. Common Crawl cuts a page at 1 MiB.
        var text = new StringBuilder(mebibytes << 20);
        var random = new Random(3);
        while (text.Length < (mebibytes << 20) - 16)
            text.Append((char)('a' + random.Next(26))).Append(' ');
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://a.example/1", "A small page of text."), WarcSynth.ConversionRecord("https://a.example/big", text.ToString())]);
        text.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var product = new ShardProcessor(Plain(), null).Process(new MemoryStream(shard), CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream());
        var elapsed = clock.Elapsed;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.RecordsIn, Is.EqualTo(2), "the skipped record is a record of the shard");
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { ("record_oversized", 1) }));
            Assert.That(stats.DocumentsOut, Is.EqualTo(1));
            Assert.That(ShardDropRules.StageOf("record_oversized"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
            Assert.That(allocated, Is.LessThan(64L << 20), "read past, never held");
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(ShardLimits.MaxRecordBytes(CorpusSourceFormat.Wet), Is.EqualTo(8 << 20));
            Assert.That(ShardLimits.MaxRecordBytes(CorpusSourceFormat.Warc), Is.EqualTo(64 << 20), "a WARC response may be a PDF of that size");
        });
    }

    [Test]
    public void M3ADocumentOfMillionsOfWordsFailsItsLengthBeforeItIsSplitTest()
    {
        // The largest WET record that is held: 3.5 million words where a document may have
        // 100,000. The quality filter split it into as many strings to say so.
        var text = new StringBuilder(7 << 20);
        var random = new Random(5);
        while (text.Length < (7 << 20) - 16)
            text.Append((char)('a' + random.Next(26))).Append(' ');
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://a.example/long", text.ToString())]);
        text.Clear();
        var pipeline = new CorpusPipelineData { Version = "m3", IdentifyLanguage = false, CleanLines = false, Sign = false };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var product = new ShardProcessor(pipeline, null).Process(new MemoryStream(shard), CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream());
        var elapsed = clock.Elapsed;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(product.Stats.Build(0, 0, 0, 0).Drops.Select(drop => drop.Name), Is.EqualTo(new[] { "max_words" }));
            Assert.That(allocated, Is.LessThan(160L << 20), "the record, its text and its lines: no string per word (it was about 900 MiB)");
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public void M3CountingWordsGivesTheNumberSplittingThemDoesTest()
    {
        string[] texts =
        [
            "", " ", "one", " one  two\tthree\nfour ", "line one\nline two\n\nline four", "tabs\tand no-break spaces　here",
            "日本語の文章 with Latin words と kana", "ภาษาไทย mixed 123 中文", "a\U0001F600b \U00020000\U00020001 c",
            WarcSynth.Paragraphs(new Random(11), 500)
        ];
        var spaced = QualityRules.Spaced;
        var unspaced = QualityRules.Unspaced;

        Assert.Multiple(() =>
        {
            foreach (var text in texts)
            {
                Assert.That(QualityFilter.WordCount(text, spaced), Is.EqualTo(QualityFilter.Words(text, spaced).Count), $"spaced: '{text}'");
                Assert.That(QualityFilter.WordCount(text, unspaced), Is.EqualTo(QualityFilter.Words(text, unspaced).Count), $"unspaced: '{text}'");
            }
        });
    }

    [Test]
    public void M3TheFirstFailedRuleIsTheOneAFullJudgementNamesFirstTest()
    {
        var rules = QualityRules.Spaced;
        string[] texts = ["too short", WarcSynth.Paragraphs(new Random(1), 300), string.Join(' ', Enumerable.Repeat("word", rules.MaxWords + 5)), string.Join(' ', Enumerable.Repeat("same line.\n", 200))];

        Assert.Multiple(() =>
        {
            foreach (var text in texts)
            {
                var full = QualityFilter.Judge(text, rules);
                var first = QualityFilter.Judge(text, rules, firstFailureOnly: true);

                Assert.That(first.Passed, Is.EqualTo(full.Passed));
                Assert.That(first.FailedRules.FirstOrDefault(), Is.EqualTo(full.FailedRules.FirstOrDefault()), "the drop's name does not change");
            }
        });
    }

    #endregion

    #region Tools

    private static CorpusPipelineData Plain()
    {
        return new CorpusPipelineData { Version = "m3", IdentifyLanguage = false, CleanLines = false, FilterQuality = false, Sign = false };
    }

    private static byte[] Gzip(long length, byte fill)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var block = new byte[1 << 20];
            Array.Fill(block, fill);
            for (long written = 0; written < length; written += block.Length)
                gzip.Write(block);
        }

        return compressed.ToArray();
    }

    #endregion
}
