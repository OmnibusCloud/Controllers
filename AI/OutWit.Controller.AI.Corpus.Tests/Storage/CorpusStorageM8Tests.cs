using OutWit.Controller.AI.Corpus.Adapters;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// Audit findings M-8 and H-8 at the storage estimate, which is what the headroom refusal is
/// held to. A run whose quality filter is on under thresholds loosened to nothing was estimated
/// at the filtered ratio and produced the unfiltered one: a manifest passed the headroom by that
/// factor. And a source whose size nobody knew was left out of the estimate, so the refusal did
/// not apply to it at all. Now a threshold the table was not measured with takes the unfiltered
/// row, and a shard of unknown size is counted at the cap the nodes hold it to.
/// </summary>
[TestFixture]
public sealed class CorpusStorageM8Tests
{
    #region Constants

    /// <summary>The head of a real WET file: the measurement below is one of real crawl pages.</summary>
    private const string WET_FIXTURE = CorpusFixtures.CRAWL_WET;
    private const string PACK_FIXTURE = CorpusFixtures.PACK;
    private const string WITLID1_FIXTURE = CorpusFixtures.WITLID1;
    private const long MIB = 1024 * 1024;

    #endregion

    #region Quality Overrides

    [Test]
    [Category(CorpusFixtures.CRAWL)]
    public void M8ThresholdsLoosenedToNothingKeepWhatAnUnfilteredRunKeepsTest()
    {
        // The head of a real WET file with the filter on. Measured, not argued: the ten thresholds a
        // manifest can set, opened wide, keep a little more than the filter's defaults (the
        // repetition rules and the stop words are not a manifest's to set); a pack whose rule
        // data opens those too keeps what a run with the filter off keeps. The switch says
        // "filtered" throughout, and the estimate was the filtered row's.
        var size = new FileInfo(Fixture(WET_FIXTURE)).Length;
        var loosened = new CorpusPipelineData { Version = "m8", Quality = Loosened() };
        var filtered = new CorpusPipelineData { Version = "m8" };
        var off = new CorpusPipelineData { Version = "m8", FilterQuality = false };
        var model = LanguageModel.Load(Fixture(PACK_FIXTURE));
        var open = WithOpenRules(model);

        var measured = Run(filtered, model);
        var byManifest = Run(loosened, model);
        var byPack = Run(loosened, open);
        var unfiltered = Run(off, model);
        var filteredRow = CorpusStorageRatios.TABLE.Single(row => row.Format == CorpusSourceFormat.Wet && row.CleanLines && row.FilterQuality);
        var taken = CorpusStorageRatios.For(CorpusSourceFormat.Wet, loosened);
        var takenForPack = CorpusStorageRatios.For(CorpusSourceFormat.Wet, filtered, CorpusLanguageModelFacts.Read(open.ToBytes()).MeasuredThresholds);

        TestContext.Out.WriteLine($"shard {size} bytes; product with the filter's defaults {measured} ({(double)measured / size:F4}), with a manifest's thresholds opened {byManifest} ({(double)byManifest / size:F4}), with a pack's rule data opened too {byPack} ({(double)byPack / size:F4}), with the filter off {unfiltered} ({(double)unfiltered / size:F4}); filtered row {filteredRow.TextPerByte}, row taken {taken.TextPerByte}");

        Assert.Multiple(() =>
        {
            Assert.That(loosened.FilterQuality, Is.True, "the switch still says filtered");
            Assert.That(byManifest, Is.GreaterThan(measured), "a manifest's own thresholds keep more than the measured filter");
            Assert.That(byPack, Is.GreaterThan(measured * 1.2), "and a pack's rule data opens the rest: far more than the measured filter keeps");
            Assert.That(byPack, Is.GreaterThanOrEqualTo(unfiltered * 0.98), "what a run with the filter off keeps");
            Assert.That(taken.FilterQuality, Is.False, "so the estimate takes the row of an unfiltered run for a manifest that sets a threshold");
            Assert.That(takenForPack.FilterQuality, Is.False, "and for a pack that does");
            Assert.That(byPack / (double)size, Is.LessThanOrEqualTo(taken.TextPerByte), "which holds the product");
        });
    }

    [Test]
    public void M8AManifestThatSetsAThresholdIsEstimatedAsUnfilteredTest()
    {
        var plain = new CorpusPipelineData();
        var one = new CorpusPipelineData { Quality = new CorpusQualityData { MinWords = 1 } };
        var curly = new CorpusPipelineData { Quality = new CorpusQualityData { FilterCurlyBracket = true } };
        var off = new CorpusPipelineData { FilterQuality = false, Quality = new CorpusQualityData { MinWords = 1 } };
        var sources = new List<CorpusSourceData> { new() { Format = CorpusSourceFormat.Wet, SizeBytes = 64 * MIB } };

        var estimated = CorpusStorageEstimator.Estimate(sources, one, 0, 0);
        var unfiltered = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData { FilterQuality = false }, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, plain).FilterQuality, Is.True);
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, one).FilterQuality, Is.False, "one threshold of the manifest's own is enough");
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Warc, one).FilterQuality, Is.False);
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, curly).FilterQuality, Is.True, "the curly-bracket switch only ever drops more");
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, off).FilterQuality, Is.False);
            Assert.That(estimated.TextBytes, Is.EqualTo(unfiltered.TextBytes));
            Assert.That(estimated.MeasuredFilter, Is.False);
            Assert.That(unfiltered.MeasuredFilter, Is.True, "a filter that is off is no filter under other thresholds");
            Assert.That(CorpusStorageCheck.Describe(estimated, CorpusStorageLimit.NONE), Does.Contain("The quality filter runs with thresholds of the manifest or of the language pack, not the ones the estimate was measured with"));
            Assert.That(CorpusStorageCheck.Describe(unfiltered, CorpusStorageLimit.NONE), Does.Not.Contain("thresholds of the manifest"));
        });
    }

    [Test]
    public void M8APackWithRuleDataOfItsOwnIsEstimatedAsUnfilteredTest()
    {
        // The reference pack and a WITLID1 model are what the table was measured with. The same
        // pack with one language's thresholds opened wide is another pack.
        var reference = CorpusLanguageModelFacts.Load(Fixture(PACK_FIXTURE));
        var witlid1 = CorpusLanguageModelFacts.Load(Fixture(WITLID1_FIXTURE));
        var own = CorpusLanguageModelFacts.Read(WithLoosenedEnglish(LanguageModel.Load(Fixture(PACK_FIXTURE))).ToBytes());
        var pipeline = new CorpusPipelineData();

        Assert.Multiple(() =>
        {
            Assert.That(reference.Problem, Is.Null);
            Assert.That(reference.MeasuredThresholds, Is.True, "the reference pack");
            Assert.That(witlid1.MeasuredThresholds, Is.True, "a WITLID1 model gets the controller's own rule data");
            Assert.That(own.Problem, Is.Null);
            Assert.That(own.MeasuredThresholds, Is.False, "a pack that sets a threshold of its own");
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, pipeline, reference.MeasuredThresholds).FilterQuality, Is.True);
            Assert.That(CorpusStorageRatios.For(CorpusSourceFormat.Wet, pipeline, own.MeasuredThresholds).FilterQuality, Is.False);
        });
    }

    #endregion

    #region Unknown Sizes

    [Test]
    public void H8ASourceWithoutAKnownSizeIsCountedAtTheCapTest()
    {
        // A thousand sources nobody could size. Left out, the estimate was the documents alone
        // and a headroom of 20 GiB refused nothing; counted at the cap it is what the nodes may send.
        var sources = Enumerable.Range(0, 1000).Select(_ => new CorpusSourceData { Format = CorpusSourceFormat.Warc }).ToList();
        var pipeline = new CorpusPipelineData { IdentifyLanguage = false };
        var headroom = new CorpusStorageLimit { HeadroomBytes = 20L << 30, Source = "test" };

        var leftOut = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0);
        var counted = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0, capBytes: 1L << 30);
        var lowerCap = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0, capBytes: 64 * MIB);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusStorageCheck.Refusal(leftOut, headroom), Is.Null, "as it was: nothing to refuse");
            Assert.That(counted.UnsizedSources, Is.EqualTo(1000));
            Assert.That(counted.UnsizedAtCap, Is.True);
            Assert.That(counted.TextBytes, Is.EqualTo(1000 * (long)Math.Ceiling((1L << 30) * CorpusStorageRatios.For(CorpusSourceFormat.Warc, pipeline).TextPerByte)));
            Assert.That(CorpusStorageCheck.Refusal(counted, headroom), Does.StartWith("Server storage: the job would leave about"), "the refusal applies to them now");
            Assert.That(CorpusStorageCheck.Describe(counted, headroom), Does.Contain("1000 source(s) without a known size are counted at the cap a node holds a shard to: declare their sizes for a closer estimate."));
            Assert.That(CorpusStorageCheck.Refusal(lowerCap, headroom), Is.Null, "a lower cap in the options is a smaller bound, and fits");
        });
    }

    [Test]
    public void H8TheEstimatesOfAPlanCountAShardNobodySizedAtTheCapTest()
    {
        // The step Split and Preflight share: three shards — declared, answered by the origin,
        // and unknown — under a cap of 100 MiB.
        var manifest = new CorpusManifest(
            new CorpusPipelineData { Version = "v", IdentifyLanguage = false },
            ["data.commoncrawl.org"],
            Enumerable.Range(0, 3).Select(index => new CorpusSourceData { Url = $"https://data.commoncrawl.org/{index}.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = index == 0 ? 10 * MIB : 0 }).ToList(),
            []);
        var options = new CorpusOptionsData { MaxShardBytes = 100 * MIB };
        var plan = CorpusTaskPlanner.Plan(manifest, options, Guid.Empty, "");
        var planned = new CorpusPlanned(plan, manifest, options, 0, null, 0);
        var sizes = new ShardSizeProbeResult(20 * MIB, 1, 0, 1) { Sizes = [10 * MIB, 20 * MIB, 0], Declared = [10 * MIB, 20 * MIB, 0], Unsized = 1 };

        var estimates = CorpusPlanning.Estimate(planned, sizes);

        Assert.Multiple(() =>
        {
            Assert.That(estimates.Counted, Is.EqualTo(new[] { 10 * MIB, 20 * MIB, 100 * MIB }), "the unknown one at the cap");
            Assert.That(estimates.Traffic.OnceBytes, Is.EqualTo(130 * MIB));
            Assert.That(estimates.Traffic.CeilingBytes, Is.EqualTo(ShardLimits.DeclaredCap(10 * MIB) + ShardLimits.DeclaredCap(20 * MIB) + 100 * MIB), "what the nodes hold each shard to");
            Assert.That(estimates.Traffic.UploadBytes, Is.EqualTo(estimates.Storage.UploadBytes), "M-8: the products are uploaded by the contributors");
            Assert.That(estimates.Storage.UploadBytes, Is.GreaterThan(0));
            Assert.That(estimates.Storage.UnsizedSources, Is.EqualTo(1));
            Assert.That(estimates.Storage.UnsizedAtCap, Is.True);
        });
    }

    #endregion

    #region Packages

    [Test]
    public void M5TheBlobsOfTheTasksAndTheResultsAreInTheEstimateTest()
    {
        // The tasks and the results travel as blobs, and blobs are storage: counted.
        var sources = Enumerable.Range(0, 1000).Select(_ => new CorpusSourceData { Format = CorpusSourceFormat.Wet, SizeBytes = 64 * MIB }).ToList();
        var pipeline = new CorpusPipelineData { Output = CorpusOutputMode.StatsAndHashes };

        var plain = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0);
        var sampled = CorpusStorageEstimator.Estimate(sources, pipeline, 0.1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(plain.PackageBytes, Is.EqualTo(1000L * (CorpusStorageRatios.PACKAGE_BASE_BYTES + CorpusStorageRatios.TASK_PACKAGE_BYTES_PER_SHARD) + 1000L * (CorpusStorageRatios.PACKAGE_BASE_BYTES + CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD)));
            Assert.That(sampled.PackageBytes - plain.PackageBytes, Is.EqualTo(100L * (CorpusStorageRatios.PACKAGE_BASE_BYTES + CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD)), "a sampled package answers twice");
            Assert.That(plain.TotalBytes, Is.EqualTo(plain.DocumentBytes + plain.PackageBytes));
            Assert.That(plain.ResultPackageBytes, Is.EqualTo(1000L * (CorpusStorageRatios.PACKAGE_BASE_BYTES + CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD)));
            Assert.That(plain.UploadBytes, Is.EqualTo(plain.ResultPackageBytes), "under statistics and hashes only the result packages are all a node uploads, and they are in the traffic estimate");
            Assert.That(CorpusStorageCheck.Describe(plain, CorpusStorageLimit.NONE), Does.Contain("task and result packages"));
        });
    }

    [TestCase(1)]
    [TestCase(98)]
    [TestCase(1024)]
    public void M5AShardInAPackageCostsNoMoreThanTheEstimateCountsTest(int shards)
    {
        // The guard of the two package constants, as the heads of a real crawl guard the table: packages
        // of a job of up to 1,024 shards (one shard each), of a hundred thousand (98) and of the
        // most a job may have (1,024), with the longest things an honest run puts in them — a
        // WARC URL of Common Crawl with its published hash, and a result with every drop rule,
        // sixteen languages, the unsupported breakdown and the PDF counts, under random hashes,
        // blob ids and tickets (which do not compress).
        var random = new Random(7);
        var pipeline = new CorpusPipelineData
        {
            Version = "cc-text-v1", LanguageModelBlobId = Guid.NewGuid(), LanguageModelSha256 = Hex(random, 32), Sign = true,
            TargetLanguages = ["ces", "deu", "eng", "fas", "fra", "ita", "jpn", "nld", "pol", "por", "rus", "spa", "tur", "ukr", "vie", "zho"],
            Quality = new CorpusQualityData { MinWords = 40, MaxDuplicateLineRatio = 0.25 }
        };
        var tasks = Enumerable.Range(0, shards).Select(index => new CorpusTaskData
        {
            TaskIndex = 100_000 + index,
            Source = new CorpusSourceData
            {
                Url = $"https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/17911700538{random.Next(10, 99)}.{random.Next(10, 99)}/warc/CC-MAIN-202610{random.Next(10, 28)}{random.Next(100000, 235959)}-202610{random.Next(10, 28)}{random.Next(100000, 235959)}-{index:D5}.warc.gz",
                Format = CorpusSourceFormat.Warc, SizeBytes = 900_000_000 + random.Next(100_000_000), Sha256 = Hex(random, 32)
            },
            Pipeline = pipeline,
            AllowedOrigins = ["data.commoncrawl.org"],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION,
            DeclaredBytes = 900_000_000 + random.Next(100_000_000)
        }).ToList();

        var results = tasks.Select(task =>
        {
            // Counters that add up as an honest node's do: the result is one the server accepts.
            var unsupported = new[] { "ara", "aze", "ben", "bul", "cat", "dan", "ell", "est", "fin", "heb", "hrv", "hun", "ind", "kor", "lav", "lit", "nor", "ron", "slk", "slv", "srp", "swe", "tha" }
                .SelectMany(code => new[] { (Name: code, Count: random.Next(1, 900)) }).Append((Name: "no_profile", Count: random.Next(1, 90)))
                .OrderBy(count => count.Name, StringComparer.Ordinal).ToList();
            var drops = ShardDropRules.ALL
                .Select(rule => (Name: rule.Rule, Count: rule.Rule == ShardProcessor.DROP_LANGUAGE_UNSUPPORTED ? unsupported.Sum(count => count.Count) : random.Next(1, 5000)))
                .OrderBy(count => count.Name, StringComparer.Ordinal).ToList();
            var languages = pipeline.TargetLanguages.Select(code => (Name: code, Count: random.Next(1, 900))).OrderBy(count => count.Name, StringComparer.Ordinal).ToList();
            var inside = drops.Where(drop => ShardDropRules.StageOf(drop.Name) != ShardDropRules.STAGE_EXTRACT).Sum(drop => drop.Count);
            var documentsOut = languages.Sum(language => language.Count);

            var result = CorpusTestRun.Completed(task);
            result.ContentSha256 = Hex(random, 32);
            result.SignaturesSha256 = Hex(random, 32);
            result.TextBlobId = Guid.NewGuid();
            result.SignaturesBlobId = Guid.NewGuid();
            result.Stats = new CorpusShardStatsData
            {
                RecordsIn = documentsOut + drops.Sum(drop => drop.Count) + random.Next(9000), DocumentsExtracted = documentsOut + inside, DocumentsOut = documentsOut,
                BytesFetched = task.DeclaredBytes, BytesProduced = 20_000_000 + random.Next(9_000_000),
                FetchMs = 20_000 + random.Next(90_000), ProcessMs = 100_000 + random.Next(90_000), WaitMs = random.Next(600_000),
                Drops = CorpusTestRun.Counts(drops),
                Languages = CorpusTestRun.Counts(languages),
                UnsupportedLanguages = CorpusTestRun.Counts(unsupported),
                Pdf = new CorpusPdfStatsData { In = 220 + random.Next(30), Truncated = random.Next(20), Extracted = 150 + random.Next(40), Pages = 3000 + random.Next(900), Documents = 700 + random.Next(200), PagesCut = random.Next(9), RunningLines = random.Next(4000), Fuel = 600_000_000_000 + random.NextInt64(90_000_000_000) }
            };
            return result;
        }).ToList();

        using var taskBlob = new MemoryStream();
        CorpusPackageCodec.WriteTasks(taskBlob, tasks[0], tasks);
        using var resultBlob = new MemoryStream();
        CorpusPackageCodec.WriteResults(resultBlob, results);

        var taskAllowance = CorpusStorageRatios.PACKAGE_BASE_BYTES + (long)shards * CorpusStorageRatios.TASK_PACKAGE_BYTES_PER_SHARD;
        var resultAllowance = CorpusStorageRatios.PACKAGE_BASE_BYTES + (long)shards * CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD;

        TestContext.Out.WriteLine($"{shards} shard(s): task package {taskBlob.Length} bytes ({(double)taskBlob.Length / shards:F1} a shard; counted {taskAllowance}), result package {resultBlob.Length} bytes ({(double)resultBlob.Length / shards:F1} a shard; counted {resultAllowance})");

        Assert.Multiple(() =>
        {
            Assert.That(taskBlob.Length, Is.LessThanOrEqualTo(taskAllowance), "a task package is inside what the estimate counts for it");
            Assert.That(resultBlob.Length, Is.LessThanOrEqualTo(resultAllowance), "and a result package");
            Assert.That(CorpusResultCheck.IsWellFormed(results[0]), Is.True, "the result measured is one the server accepts");
        });
    }

    #endregion

    #region Tools

    private static string Hex(Random random, int bytes)
    {
        var buffer = new byte[bytes];
        random.NextBytes(buffer);
        return Convert.ToHexStringLower(buffer);
    }

    /// <summary>Every threshold a manifest can set, opened as wide as it goes.</summary>
    private static CorpusQualityData Loosened()
    {
        return new CorpusQualityData
        {
            MinWords = 1,
            MaxWords = int.MaxValue,
            MinMeanWordLength = 0.001,
            MaxMeanWordLength = 1e9,
            MaxSymbolToWordRatio = 1e9,
            MaxBulletLineRatio = 1e9,
            MaxEllipsisLineRatio = 1e9,
            MinAlphabeticWordRatio = 0.000001,
            MaxDuplicateLineRatio = 1e9,
            MaxDuplicateLineCharRatio = 1e9
        };
    }

    /// <summary>The same pack with every language's rule data opened wide: no stop word asked for, the repetition rules past any text.</summary>
    private static LanguageModel WithOpenRules(LanguageModel pack)
    {
        var rules = pack.Rules.Languages.Select(language => new LanguageRules(language.Code, language.Script, language.StopWords, 0,
        [
            new KeyValuePair<string, IReadOnlyList<double>>(OutWit.Controller.AI.Corpus.Quality.QualityRuleOverrides.TOP_NGRAM_CHARS, [1e9, 1e9, 1e9]),
            new KeyValuePair<string, IReadOnlyList<double>>(OutWit.Controller.AI.Corpus.Quality.QualityRuleOverrides.DUPLICATE_NGRAM_CHARS, [1e9, 1e9, 1e9, 1e9, 1e9, 1e9])
        ]));

        var limits = pack.Languages.ToDictionary(code => code, code => pack.Acceptance!.MaxDistancePpm(code), StringComparer.Ordinal);

        return new LanguageModel("open-rules", pack.Profiles, new LanguageRuleSet(rules), limits, pack.Metadata);
    }

    /// <summary>The same pack with English's own thresholds opened wide: a pack with rule data of its own.</summary>
    private static LanguageModel WithLoosenedEnglish(LanguageModel pack)
    {
        var rules = pack.Rules.Languages.Select(language => language.Code != "eng"
            ? language
            : new LanguageRules(language.Code, language.Script, language.StopWords, 0, [new KeyValuePair<string, IReadOnlyList<double>>("min_words", [1])]));

        var limits = pack.Languages.ToDictionary(code => code, code => pack.Acceptance!.MaxDistancePpm(code), StringComparer.Ordinal);

        return new LanguageModel("own-thresholds", pack.Profiles, new LanguageRuleSet(rules), limits, pack.Metadata);
    }

    private static long Run(CorpusPipelineData pipeline, LanguageModel model)
    {
        var processor = new ShardProcessor(pipeline, model);
        using var shard = File.OpenRead(Fixture(WET_FIXTURE));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();
        processor.Process(shard, CorpusSourceFormat.Wet, text, signatures);
        return text.Length;
    }

    private static string Fixture(string name)
    {
        return CorpusFixtures.PathOf(name);
    }

    #endregion
}
