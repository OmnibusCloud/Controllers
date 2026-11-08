using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>The manifest parser: the full form, the bare-URL form, and every way a manifest can be wrong.</summary>
[TestFixture]
public sealed class CorpusManifestParserTests
{
    [Test]
    public void FullManifestParsesTest()
    {
        var manifest = CorpusManifestParser.Parse("""
            {
              "pipeline": {
                "version": "cc-text-v1",
                "language_model_sha256": "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f",
                "target_languages": ["eng", "deu"],
                "identify_language": true,
                "clean_lines": false,
                "filter_quality": true,
                "sign": false,
                "quality": { "min_words": 80, "max_duplicate_line_ratio": 0.25 }
              },
              "allowed_origins": ["data.commoncrawl.org"],
              "sources": [
                "https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/1/wet/CC-MAIN-00000.warc.wet.gz",
                { "url": "https://data.commoncrawl.org/x.bin", "format": "warc", "size": 1234, "sha256": "ab" }
              ]
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Is.EqualTo(new[] { "sources[1]: 'sha256' must be 64 hex digits." }));
            Assert.That(manifest.Pipeline.Version, Is.EqualTo("cc-text-v1"));
            Assert.That(manifest.Pipeline.LanguageModelSha256, Is.EqualTo("9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f"));
            Assert.That(manifest.Pipeline.TargetLanguages, Is.EqualTo(new[] { "eng", "deu" }));
            Assert.That(manifest.Pipeline.Sign, Is.False);
            Assert.That(manifest.Pipeline.CleanLines, Is.False);
            Assert.That(manifest.Pipeline.Quality!.MinWords, Is.EqualTo(80));
            Assert.That(manifest.Pipeline.Quality.MaxDuplicateLineRatio, Is.EqualTo(0.25));
            Assert.That(manifest.Pipeline.Quality.MaxWords, Is.Zero, "unset thresholds stay 0 = default");
            Assert.That(manifest.AllowedOrigins, Is.EqualTo(new[] { "data.commoncrawl.org" }));
            Assert.That(manifest.Sources, Has.Count.EqualTo(2));
            Assert.That(manifest.Sources[0].Format, Is.EqualTo(CorpusSourceFormat.Wet));
            Assert.That(manifest.Sources[1].Format, Is.EqualTo(CorpusSourceFormat.Warc));
            Assert.That(manifest.Sources[1].SizeBytes, Is.EqualTo(1234));
        });
    }

    [Test]
    public void MinimalManifestHasDefaultsTest()
    {
        var manifest = CorpusManifestParser.Parse("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Is.Empty);
            Assert.That(manifest.Pipeline.IdentifyLanguage, Is.True);
            Assert.That(manifest.Pipeline.FilterQuality, Is.True);
            Assert.That(manifest.Pipeline.Sign, Is.True);
            Assert.That(manifest.Pipeline.CleanLines, Is.True, "the line stage is on unless the manifest turns it off");
            Assert.That(manifest.Pipeline.Quality, Is.Null);
            Assert.That(manifest.Pipeline.TargetLanguages, Is.Empty);
        });
    }

    [TestCase("", "not valid JSON")]
    [TestCase("[]", "must be a JSON object")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"]}""", "'sources' is required")]
    [TestCase("""{"pipeline":{"version":"v"},"sources":["https://h/a.warc.gz"]}""", "'allowed_origins' is required")]
    [TestCase("""{"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "'pipeline' is required")]
    [TestCase("""{"pipeline":{},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "'version' is required")]
    [TestCase("""{"pipeline":{"version":"v","identify_languages":true},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "unknown key 'identify_languages'")]
    [TestCase("""{"pipeline":{"version":"v","sign":"yes"},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "'sign' must be true or false")]
    [TestCase("""{"pipeline":{"version":"v","quality":{"min_word":5}},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "unknown key 'min_word'")]
    [TestCase("""{"pipeline":{"version":"v","quality":{"min_words":-5}},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "must be a number of 0 or more")]
    [TestCase("""{"pipeline":{"version":"v","language_model_sha256":"xyz"},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"]}""", "must be 64 hex digits")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":"h","sources":["https://h/a.warc.gz"]}""", "must be an array of strings")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[]}""", "names no shard")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[42]}""", "must be a URL string or an object")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":["https://h/a.bin"]}""", "cannot tell the format")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[{"url":"https://h/a.bin","format":"parquet"}]}""", "'format' must be 'wet' or 'warc'")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[{"format":"wet"}]}""", "'url' is required")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":[{"url":"https://h/a.warc.gz","sizes":1}]}""", "unknown key 'sizes'")]
    [TestCase("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":["https://h/a.warc.gz"],"source":[]}""", "unknown key 'source'")]
    public void ProblemsAreReportedTest(string json, string expected)
    {
        var manifest = CorpusManifestParser.Parse(json);

        Assert.That(manifest.Errors, Has.Some.Contains(expected), string.Join(" | ", manifest.Errors));
    }

    [Test]
    public void ByteOrderMarkAndCommentsAreToleratedTest()
    {
        var manifest = CorpusManifestParser.Parse("﻿{ // the pipeline\n\"pipeline\":{\"version\":\"v\"},\"allowed_origins\":[\"h\"],\"sources\":[\"https://h/a.warc.wet.gz\",],}");

        Assert.That(manifest.Errors, Is.Empty);
    }

    [TestCase("https://h/a.warc.wet.gz", CorpusSourceFormat.Wet)]
    [TestCase("https://h/a.WARC.WET.GZ", CorpusSourceFormat.Wet)]
    [TestCase("https://h/a.warc.gz?x=1", CorpusSourceFormat.Warc)]
    [TestCase("https://h/a.wet.gz", null)]
    [TestCase("https://h/a.warc", null)]
    public void FormatFollowsTheExtensionTest(string url, CorpusSourceFormat? expected)
    {
        Assert.That(CorpusManifestParser.FormatFromUrl(url), Is.EqualTo(expected));
    }
}
