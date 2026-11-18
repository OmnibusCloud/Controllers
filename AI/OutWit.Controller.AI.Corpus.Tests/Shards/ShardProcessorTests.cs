using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The pipeline over the generated shards, and over the heads of a real crawl where a product
/// of real data is pinned: the product is a function of the shard and the pipeline (same hash
/// twice, on WET and on WARC), its JSON lines decompress to what the hash covers, the signatures
/// line up with the documents, the counters add up, and each stage switch does what it says. The
/// default model is the reference language pack; the 0.1.x model (WITLID1) still gives the
/// products 0.1.14 gave on the real heads. The cases of the real heads carry the category
/// <c>Crawl</c>: they fetch the heads by range and are ignored where the origin cannot be reached.
/// </summary>
[TestFixture]
public sealed class ShardProcessorTests
{
    #region Constants

    private const string WET_FIXTURE = CorpusFixtures.WET;
    private const string WARC_FIXTURE = CorpusFixtures.WARC;
    private const string CRAWL_WET = CorpusFixtures.CRAWL_WET;
    private const string CRAWL_WARC = CorpusFixtures.CRAWL_WARC;
    private const string MODEL_FIXTURE = CorpusFixtures.PACK;
    private const string WITLID1_FIXTURE = CorpusFixtures.WITLID1;

    /// <summary>Drops that happen before a document counts as extracted.</summary>
    private static readonly HashSet<string> PRE_EXTRACTION =
    [
        ShardProcessor.DROP_HTTP_UNPARSABLE, ShardProcessor.DROP_HTTP_STATUS, ShardProcessor.DROP_NOT_HTML,
        ShardProcessor.DROP_HTML_REFUSED, ShardProcessor.DROP_EMPTY_TEXT
    ];

    #endregion

    #region Fields

    private LanguageModel m_model = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(Fixture(MODEL_FIXTURE));
    }

    #endregion

    #region Determinism Tests

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    [TestCase(CRAWL_WET, CorpusSourceFormat.Wet, Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, CorpusSourceFormat.Warc, Category = CorpusFixtures.CRAWL)]
    public void SameShardSamePipelineGivesSameProductTest(string fixture, CorpusSourceFormat format)
    {
        var pipeline = DefaultPipeline();
        var first = Run(fixture, format, pipeline);
        var second = Run(fixture, format, pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(first.Product.ContentSha256, Is.EqualTo(second.Product.ContentSha256));
            Assert.That(first.Text, Is.EqualTo(second.Text), "the compressed bytes differ");
            Assert.That(first.Signatures, Is.EqualTo(second.Signatures));
            Assert.That(first.Product.Stats.Build(0, 0, 0, 0).Is(second.Product.Stats.Build(0, 0, 0, 0)), Is.True);
            Assert.That(first.Product.Stats.DocumentsOut, Is.GreaterThan(20), "the shard should keep a meaningful share");
        });
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void ContentHashCoversTheDecompressedLinesTest(string fixture, CorpusSourceFormat format)
    {
        var run = Run(fixture, format, DefaultPipeline());
        var lines = CorpusCompressor.Decompress(run.Text);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(lines)), Is.EqualTo(run.Product.ContentSha256));
            Assert.That(Lines(lines), Has.Count.EqualTo(run.Product.Stats.DocumentsOut));
        });
    }

    [Test]
    public void EveryLineIsAnObjectWithUrlLangAndTextTest()
    {
        var run = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());

        foreach (var line in Lines(CorpusCompressor.Decompress(run.Text)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "url", "lang", "text" }));
                Assert.That(root.GetProperty("url").GetString(), Does.StartWith("http"));
                Assert.That(root.GetProperty("lang").GetString(), Has.Length.EqualTo(3));
                Assert.That(root.GetProperty("text").GetString(), Is.Not.Empty);
            });
        }
    }

    [Test]
    public void SignaturesLineUpWithTheDocumentsTest()
    {
        var run = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var signatures = ShardSignatureWriter.Read(run.Signatures);
        var texts = Lines(CorpusCompressor.Decompress(run.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();

        Assert.That(signatures, Has.Count.EqualTo(texts.Count));
        for (var i = 0; i < texts.Count; i++)
            Assert.That(signatures[i], Is.EqualTo(MinHashSigner.Sign(texts[i])), $"signature {i} is not the document's");
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void ConcurrentShardsGiveTheSingleRunsProductTest(string fixture, CorpusSourceFormat format)
    {
        // A node runs several shards at once in one process: nothing a stage shares between
        // threads (the HTML parser, the language model, the signer's coefficients) may move a product.
        var single = Run(fixture, format, DefaultPipeline()).Product.ContentSha256;
        var hashes = new string[8];
        var workers = Enumerable.Range(0, hashes.Length).Select(i => new Thread(() => hashes[i] = Run(fixture, format, DefaultPipeline()).Product.ContentSha256)).ToList();
        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());

        Assert.That(hashes, Is.All.EqualTo(single));
    }

    /// <summary>
    /// The products of the shards under the default pipeline, pinned: produced on win-x64,
    /// re-checked on every platform the workflow runs, so a stage that read a platform (a Unicode
    /// table, a culture, a hash seed) would show here. A change to any stage moves them; pin the
    /// new values with the change that moved them. The WARC shards' products here are the ones
    /// with the PDF stage off, the language pack's product from before the stage existed; with
    /// the stage on, below. The first two are the generated shards (they move with the generator
    /// as well); the last two are the heads of the real crawl, the same values since the language
    /// pack: a release that moves them moves what an honest run makes.
    /// </summary>
    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet, "1a8e1ecb509fa6bb2174de5ef411261952c11d1e51debf68e098fb3eccb5c2cd")]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc, "d5bede2c696fbf6fc0620d8d9966026d80c709b90f2596ac14a62882d21fb76c")]
    [TestCase(CRAWL_WET, CorpusSourceFormat.Wet, "5fd9af0b09764d3b017cf15c1338b56271faf9f0e857efa653c4dc71a2d3e74b", Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, CorpusSourceFormat.Warc, "8ca081e1365d099006385c551dd17fb0536841fd2632f9547f5c1fd7165f1f90", Category = CorpusFixtures.CRAWL)]
    public void GoldenShardProductIsPinnedTest(string fixture, CorpusSourceFormat format, string expected)
    {
        var product = Run(fixture, format, new CorpusPipelineData { Version = "test-v1", ExtractPdf = false }).Product;

        Assert.That(product.ContentSha256, Is.EqualTo(expected), $"{fixture}: the product hashes to {product.ContentSha256}");
    }

    /// <summary>
    /// A manifest pinned to the 0.1.x model (WITLID1) keeps its products: the real heads'
    /// products under Corpus 0.1.14 (its pins, unchanged; the PDF stage off, as 0.1.14 had none),
    /// with the rule data 0.1.14 had in code now read from data, and no document called
    /// unsupported. The generated shards are pinned under the same model: there it keeps, under a
    /// language it has, pages the pack calls unsupported, so its products are others than the pack's.
    /// </summary>
    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet, "058f3940239bc16afe1d658a9903aa49431bf22308b9a86356a602fe28c37a74")]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc, "041aab43f48129699ece818d5d98817976fe312f2f9905dfe640355e852f41b6")]
    [TestCase(CRAWL_WET, CorpusSourceFormat.Wet, "61ac69cf399a57c75a127b86dc85c02534fc51de4e0e82a896b3de647f735620", Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, CorpusSourceFormat.Warc, "f0bedbac8df708e48cae4e181f4b8beeef07ea083ac9cc9babd1e3423f1b6690", Category = CorpusFixtures.CRAWL)]
    public void AWitlid1ModelKeepsTheProductsOf0114Test(string fixture, CorpusSourceFormat format, string expected)
    {
        var witlid1 = LanguageModel.Load(Fixture(WITLID1_FIXTURE));
        var run = Run(fixture, format, new CorpusPipelineData { Version = "test-v1", ExtractPdf = false }, witlid1);
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(witlid1.Format, Is.EqualTo(LanguageModelFormat.WITLID1));
            Assert.That(run.Product.ContentSha256, Is.EqualTo(expected), $"{fixture}: the product hashes to {run.Product.ContentSha256}");
            Assert.That(stats.Drops.Select(drop => drop.Name), Has.None.EqualTo(ShardProcessor.DROP_LANGUAGE_UNSUPPORTED));
        });
    }

    /// <summary>
    /// Each WARC shard holds one PDF response of one page (the generated shard a report of the
    /// tests' own making, the real head a dance sheet): with the PDF stage on, its document
    /// passes every stage and joins the product, under the 0.1.x model (the PDF stage's own pin)
    /// and under the language pack (where the two meet). Needs the extractor module.
    /// </summary>
    [TestCase(WARC_FIXTURE, WITLID1_FIXTURE, "b9fe2d3ceae409b72f3bb4705e03aec1a47781bc031060bbc6e0cb531a9345d4")]
    [TestCase(WARC_FIXTURE, MODEL_FIXTURE, "bd75043d87141d8afb7654d410845e29f9a439131d2c8c58e896ba987b7c4173")]
    [TestCase(CRAWL_WARC, WITLID1_FIXTURE, "d4f7f075894df9b856e4702bf0eedf0698bb664c97db35613839500c41421fee", Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, MODEL_FIXTURE, "40469b64d312f67a4055b838e767794d8e455ff554fc6eaf57fd4df373e108f1", Category = CorpusFixtures.CRAWL)]
    public void GoldenWarcProductWithThePdfStageIsPinnedTest(string fixture, string model, string expected)
    {
        PdfTestModule.Require();
        var run = Run(fixture, CorpusSourceFormat.Warc, DefaultPipeline(), LanguageModel.Load(Fixture(model)));
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(run.Product.ContentSha256, Is.EqualTo(expected), $"{fixture}, {model}: the product hashes to {run.Product.ContentSha256}");
            Assert.That(stats.Pdf!.In, Is.EqualTo(1));
            Assert.That(stats.Pdf.Extracted, Is.EqualTo(1));
            Assert.That(Lines(CorpusCompressor.Decompress(run.Text)).Count(line => line.Contains(".pdf#page=1-1\"", StringComparison.Ordinal)), Is.EqualTo(1));
        });
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    [TestCase(CRAWL_WET, CorpusSourceFormat.Wet, Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, CorpusSourceFormat.Warc, Category = CorpusFixtures.CRAWL)]
    public void ThePackOnlyAddsUnknownToTheWitlid1ProductTest(string fixture, CorpusSourceFormat format)
    {
        // The pack's supported profiles and rule data are the 0.1.x model's: every document it
        // keeps, the old model kept with the same label and text; what it adds is
        // language_unsupported, taken from the documents the old model labelled.
        var pack = Run(fixture, format, DefaultPipeline());
        var witlid1 = Run(fixture, format, DefaultPipeline(), LanguageModel.Load(Fixture(WITLID1_FIXTURE)));

        var packLines = Lines(CorpusCompressor.Decompress(pack.Text));
        var oldLines = Lines(CorpusCompressor.Decompress(witlid1.Text));
        var packStats = pack.Product.Stats.Build(0, 0, 0, 0);
        var oldStats = witlid1.Product.Stats.Build(0, 0, 0, 0);
        var unsupported = packStats.Drops.SingleOrDefault(drop => drop.Name == ShardProcessor.DROP_LANGUAGE_UNSUPPORTED)?.Count ?? 0;

        TestContext.Out.WriteLine($"{fixture}: {oldLines.Count} -> {packLines.Count} documents out, {unsupported} unsupported");
        Assert.Multiple(() =>
        {
            Assert.That(oldLines, Is.SupersetOf(packLines), "nothing new, nothing relabelled");
            Assert.That(packStats.DocumentsExtracted, Is.EqualTo(oldStats.DocumentsExtracted));
            Assert.That(Count(packStats, ShardProcessor.DROP_LANGUAGE_UNKNOWN), Is.EqualTo(Count(oldStats, ShardProcessor.DROP_LANGUAGE_UNKNOWN)), "too little text is too little text");
            Assert.That(unsupported, Is.GreaterThanOrEqualTo(oldLines.Count - packLines.Count));
        });
    }

    #endregion

    #region Stage Tests

    [TestCase(WET_FIXTURE, 430)]
    [TestCase(CRAWL_WET, 437, Category = CorpusFixtures.CRAWL)]
    public void CountersAddUpTest(string fixture, int records)
    {
        var run = Run(fixture, CorpusSourceFormat.Wet, DefaultPipeline());
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.RecordsIn, Is.EqualTo(records), "every record of the shard is counted");
            Assert.That(stats.DocumentsExtracted + stats.Drops.Where(d => PRE_EXTRACTION.Contains(d.Name)).Sum(d => d.Count), Is.LessThanOrEqualTo(stats.RecordsIn));
            Assert.That(stats.DocumentsOut + stats.Drops.Where(d => !PRE_EXTRACTION.Contains(d.Name)).Sum(d => d.Count), Is.EqualTo(stats.DocumentsExtracted), "every extracted document is out or dropped by a later stage");
            Assert.That(stats.Languages.Sum(l => l.Count), Is.EqualTo(stats.DocumentsOut));
            Assert.That(stats.Drops.Select(d => d.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(stats.Languages.Select(l => l.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
        });
    }

    [Test]
    public void TargetLanguagesKeepOnlyThoseTest()
    {
        var all = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var english = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", TargetLanguages = ["eng"] });

        var languages = english.Product.Stats.Build(0, 0, 0, 0).Languages;
        var englishInAll = all.Product.Stats.Build(0, 0, 0, 0).Languages.Single(l => l.Name == "eng").Count;

        Assert.Multiple(() =>
        {
            Assert.That(languages.Select(l => l.Name), Is.EqualTo(new[] { "eng" }));
            Assert.That(languages[0].Count, Is.EqualTo(englishInAll));
            // The language stage runs before the quality filter, so it also drops the non-English
            // documents that quality would have dropped: at least every non-English survivor.
            Assert.That(english.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == ShardProcessor.DROP_LANGUAGE_NOT_TARGET).Count, Is.GreaterThanOrEqualTo(all.Product.Stats.DocumentsOut - englishInAll));
        });
    }

    [Test]
    public void StagesSwitchOffTest()
    {
        var raw = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = false, FilterQuality = false, Sign = false }, model: null);
        var stats = raw.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.DocumentsOut, Is.EqualTo(stats.DocumentsExtracted), "nothing filters, everything extracted goes out");
            Assert.That(stats.Languages.Select(l => l.Name), Is.EqualTo(new[] { "" }));
            Assert.That(raw.Signatures, Is.Empty);
            Assert.That(Lines(CorpusCompressor.Decompress(raw.Text)).All(line => line.Contains("\"lang\":\"\"")), Is.True);
        });
    }

    [TestCase(WET_FIXTURE)]
    [TestCase(CRAWL_WET, Category = CorpusFixtures.CRAWL)]
    public void LineStageKeepsLinesAndCleansThemTest(string fixture)
    {
        var raw = Run(fixture, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = false, FilterQuality = false, Sign = false });
        var cleaned = Run(fixture, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = true, FilterQuality = false, Sign = false });

        var rawTexts = Lines(CorpusCompressor.Decompress(raw.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();
        var cleanTexts = Lines(CorpusCompressor.Decompress(cleaned.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rawTexts.Count(text => text.Contains('\n')), Is.GreaterThan(rawTexts.Count * 9 / 10), "WET documents keep their lines");
            Assert.That(cleaned.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == ShardProcessor.DROP_NO_PROSE_LINES).Count, Is.GreaterThan(0), "pages without a prose line drop");
            Assert.That(cleanTexts.Sum(text => text.Length), Is.LessThan(rawTexts.Sum(text => text.Length) * 3 / 4), "the stage removes a real share of the text");
            Assert.That(cleanTexts.SelectMany(text => text.Split('\n')).All(line => LineCleaner.Keep(line, QualityRules.Spaced)), Is.True, "every line left reads like prose");
        });
    }

    [Test]
    public void QualityOverridesTightenTheFilterTest()
    {
        var loose = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var strict = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", Quality = new CorpusQualityData { MinWords = 500 } });

        Assert.That(strict.Product.Stats.DocumentsOut, Is.LessThan(loose.Product.Stats.DocumentsOut));
        Assert.That(strict.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == "min_words").Count, Is.GreaterThan(loose.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == "min_words").Count));
    }

    [Test]
    public void ALanguageThePackDoesNotSupportDropsAsUnsupportedTest()
    {
        const string english =
            "The committee reviewed the annual report in detail and approved the budget for the coming year.\n" +
            "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund.\n" +
            "The chair thanked the volunteers for their work on the summer programme and the autumn fair.";
        const string swedish =
            "Kommunen öppnar det nya biblioteket nästa vecka, när renoveringen är klar.\n" +
            "Barnavdelningen flyttar till andra våningen och läsesalen får nya stolar.\n" +
            "Kaféet har öppet varje dag från morgon till kväll, och entrén är gratis.";
        var shard = WarcSynth.Shard([
            WarcSynth.ConversionRecord("https://example.test/en", english),
            WarcSynth.ConversionRecord("https://example.test/sv", swedish),
            WarcSynth.ConversionRecord("https://example.test/short", "Hej då!")
        ]);
        var pipeline = new CorpusPipelineData { Version = "test-v1", CleanLines = false, FilterQuality = false, Sign = false };

        var pack = RunBytes(shard, pipeline, m_model);
        var witlid1 = RunBytes(shard, pipeline, LanguageModel.Load(Fixture(WITLID1_FIXTURE)));

        Assert.Multiple(() =>
        {
            Assert.That(pack.Drops.Select(drop => (drop.Name, drop.Count)), Is.EquivalentTo(new[] { (ShardProcessor.DROP_LANGUAGE_UNKNOWN, 1), (ShardProcessor.DROP_LANGUAGE_UNSUPPORTED, 1) }));
            Assert.That(pack.Languages.Select(language => (language.Name, language.Count)), Is.EqualTo(new[] { ("eng", 1) }));
            Assert.That(pack.UnsupportedLanguages.Select(language => (language.Name, language.Count)), Is.EqualTo(new[] { ("swe", 1) }), "the Swedish page, named");
            Assert.That(witlid1.UnsupportedLanguages, Is.Empty);
            Assert.That(witlid1.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { (ShardProcessor.DROP_LANGUAGE_UNKNOWN, 1) }), "0.1.14 labels the Swedish page with a language it has");
            Assert.That(witlid1.DocumentsOut, Is.EqualTo(2));
        });
    }

    [Test]
    public void TheUnsupportedBreakdownNamesWhereEachDocumentWasNearestTest()
    {
        // A known unsupported language (Danish), a script no profile has (Armenian: no n-gram in
        // common, "nearest" would be a tie), and the generated shard's own mix.
        const string danish =
            "Kommunen åbner det nye bibliotek i næste uge, når renoveringen er færdig.\n" +
            "Børneafdelingen flytter til anden sal, og læsesalen får nye stole.";
        const string armenian = "Քաղաքապետարանը հաջորդ շաբաթ կբացի նոր գրադարանը, երբ վերանորոգումն ավարտվի։";
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://example.test/da", danish), WarcSynth.ConversionRecord("https://example.test/hy", armenian)]);
        var pipeline = new CorpusPipelineData { Version = "test-v1", CleanLines = false, FilterQuality = false, Sign = false };

        var synthetic = RunBytes(shard, pipeline, m_model);
        var golden = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline()).Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(synthetic.UnsupportedLanguages.Select(language => (language.Name, language.Count)), Is.EqualTo(new[] { ("dan", 1), (ShardStatsBuilder.UNSUPPORTED_NO_PROFILE, 1) }));
            Assert.That(golden.UnsupportedLanguages.Sum(language => language.Count), Is.EqualTo(Count(golden, ShardProcessor.DROP_LANGUAGE_UNSUPPORTED)), "the breakdown adds up to the drop");
            Assert.That(golden.UnsupportedLanguages.Select(language => language.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(golden.Languages.Select(language => language.Name), Is.SubsetOf(m_model.Languages), "the histogram of documents out is untouched");
        });
    }

    [Test]
    public void TheCurlyBracketRuleIsAManifestSwitchOffByDefaultTest()
    {
        const string template =
            "The page builder left its template behind, and every heading shows {title} instead of a name.\n" +
            "Readers wrote to the editor about it, but the site has not been fixed in the months since then.";
        const string prose =
            "The committee reviewed the annual report in detail and approved the budget for the coming year.\n" +
            "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund.";
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://example.test/a", template), WarcSynth.ConversionRecord("https://example.test/b", prose)]);

        var off = RunBytes(shard, new CorpusPipelineData { Version = "test-v1", FilterQuality = false, Sign = false }, m_model);
        var on = RunBytes(shard, new CorpusPipelineData { Version = "test-v1", FilterQuality = false, Sign = false, Quality = new CorpusQualityData { FilterCurlyBracket = true } }, m_model);

        Assert.Multiple(() =>
        {
            Assert.That(new CorpusQualityData().FilterCurlyBracket, Is.False, "off by default");
            Assert.That(off.DocumentsOut, Is.EqualTo(2));
            Assert.That(off.Drops, Is.Empty);
            Assert.That(on.DocumentsOut, Is.EqualTo(1));
            Assert.That(on.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { (ShardProcessor.DROP_CURLY_BRACKET, 1) }));
        });
    }

    [Test]
    public void LanguageStageWithoutModelIsRefusedTest()
    {
        Assert.Throws<ArgumentException>(() => new ShardProcessor(DefaultPipeline(), null));
    }

    [Test]
    public void GarbageIsMalformedTest()
    {
        var processor = new ShardProcessor(DefaultPipeline(), m_model);
        using var shard = new MemoryStream(Encoding.ASCII.GetBytes("not a gzip stream at all"));

        Assert.That(() => processor.Process(shard, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream()), Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<WarcFormatException>());
    }

    [Test]
    public void CancellationStopsBetweenRecordsTest()
    {
        var processor = new ShardProcessor(DefaultPipeline(), m_model);
        using var shard = File.OpenRead(Fixture(WET_FIXTURE));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => processor.Process(shard, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream(), cancelled.Token));
    }

    #endregion

    #region Tools

    private (ShardProduct Product, byte[] Text, byte[] Signatures) Run(string fixture, CorpusSourceFormat format, CorpusPipelineData pipeline, LanguageModel? model = null)
    {
        // Each WARC shard holds a PDF: with the stage on, a process without the module makes no product of it.
        if (format == CorpusSourceFormat.Warc && pipeline.ExtractPdf)
            PdfTestModule.Require();

        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? model ?? m_model : null);
        using var shard = File.OpenRead(Fixture(fixture));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();

        var product = processor.Process(shard, format, text, signatures);
        return (product, text.ToArray(), signatures.ToArray());
    }

    private static CorpusShardStatsData RunBytes(byte[] shard, CorpusPipelineData pipeline, LanguageModel model)
    {
        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? model : null);
        using var input = new MemoryStream(shard);
        return processor.Process(input, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream()).Stats.Build(0, 0, 0, 0);
    }

    private static int Count(CorpusShardStatsData stats, string drop)
    {
        return stats.Drops.SingleOrDefault(count => count.Name == drop)?.Count ?? 0;
    }

    private static CorpusPipelineData DefaultPipeline()
    {
        return new CorpusPipelineData { Version = "test-v1" };
    }

    private static List<string> Lines(byte[] jsonl)
    {
        return Encoding.UTF8.GetString(jsonl).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Fixture(string name)
    {
        return CorpusFixtures.PathOf(name);
    }

    #endregion
}
