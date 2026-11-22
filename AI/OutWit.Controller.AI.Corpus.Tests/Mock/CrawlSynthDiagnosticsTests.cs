using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A diagnostic for whoever changes the generator or its text (Explicit: run by hand,
/// <c>--filter Category=Diagnostics</c>): what the generated shards are and what the pipeline
/// makes of them — sizes, the pages by kind and language, the products' hashes and counters under
/// both language models, the drops by rule. The pins of the generated shards are read off it.
/// </summary>
[TestFixture]
[Explicit("Diagnostics: prints the profile of the generated shards and of their products")]
[Category("Diagnostics")]
public sealed class CrawlSynthDiagnosticsTests
{
    #region Tests

    [Test]
    public void PrintTheProfileOfTheGeneratedShardsTest()
    {
        var pages = CrawlSynth.Pages(CorpusFixtures.Folder);
        var text = new StringBuilder();

        text.AppendLine($"SYNTH| pages {pages.Count}; by kind: {string.Join(", ", pages.GroupBy(page => page.Kind).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}"))}");
        text.AppendLine($"SYNTH| by language: {string.Join(", ", pages.GroupBy(page => page.Language).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{(group.Key.Length == 0 ? "-" : group.Key)} {group.Count()}"))}");
        text.AppendLine($"SYNTH| WARC pages by charset: {string.Join(", ", pages.Take(CrawlSynthShards.WARC_PAGES).GroupBy(page => $"{page.Charset}/{page.CharsetPlace}").OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}"))}; unclosed {pages.Take(CrawlSynthShards.WARC_PAGES).Count(page => page.Unclosed)}");

        // What the two models call each page that has one label, against the language it was written in.
        foreach (var model in new[] { CorpusFixtures.PACK, CorpusFixtures.WITLID1 })
        {
            var identifier = new LanguageIdentifier(LanguageModel.Load(CorpusFixtures.PathOf(model)));
            var labelled = pages.Where(page => page.Label != null && !page.Label.Contains(',')).ToList();
            var guesses = labelled.Select(page => (page.Language, page.Kind, Guess: identifier.Identify(CrawlSynthShards.Text(page)))).ToList();
            var supported = guesses.Where(guess => CrawlSynth.SUPPORTED.Contains(guess.Language)).ToList();
            var wrong = supported.Where(guess => guess.Guess.Code != guess.Language).Select(guess => $"{guess.Language}/{guess.Kind}->{(guess.Guess.IsKnown ? guess.Guess.Code : guess.Guess.IsUnsupported ? "unsupported" : "?")}");
            var others = guesses.Where(guess => !CrawlSynth.SUPPORTED.Contains(guess.Language)).GroupBy(guess => $"{guess.Language}->{(guess.Guess.IsKnown ? guess.Guess.Code : guess.Guess.IsUnsupported ? "unsupported" : "?")}").Select(group => $"{group.Key} {group.Count()}");

            text.AppendLine($"SYNTH| {model}: {supported.Count} pages of a supported language with one label, {supported.Count(guess => guess.Guess.Code == guess.Language)} called by it; not: {string.Join(" ", wrong)}");
            text.AppendLine($"SYNTH| {model}: the others: {string.Join(", ", others.Order(StringComparer.Ordinal))}");
        }

        foreach (var (name, format) in new[] { (CorpusFixtures.WET, CorpusSourceFormat.Wet), (CorpusFixtures.WARC, CorpusSourceFormat.Warc) })
        {
            var path = CorpusFixtures.PathOf(name);
            using (var reader = WarcReader.Open(path))
            {
                var records = reader.ReadRecords().ToList();
                text.AppendLine($"SYNTH| {name}: {new FileInfo(path).Length} bytes, {records.Count} records ({string.Join(", ", records.GroupBy(record => record.Type).Select(group => $"{group.Key} {group.Count()}"))}), {records.Sum(record => (long)record.Payload.Length)} payload bytes");
            }

            foreach (var model in new[] { CorpusFixtures.PACK, CorpusFixtures.WITLID1 })
            foreach (var pdf in format == CorpusSourceFormat.Warc ? new[] { false, true } : [false])
            {
                if (pdf && PdfTestModule.Path == null)
                    continue;

                var processor = new ShardProcessor(new CorpusPipelineData { Version = "test-v1", ExtractPdf = pdf }, LanguageModel.Load(CorpusFixtures.PathOf(model)));
                using var shard = File.OpenRead(path);
                using var product = new MemoryStream();
                using var signatures = new MemoryStream();
                var result = processor.Process(shard, format, product, signatures);
                var stats = result.Stats.Build(0, product.Length, 0, 0);

                text.AppendLine($"SYNTH| {name} {model} pdf={pdf}: content {result.ContentSha256} signatures {result.SignaturesSha256}");
                text.AppendLine($"SYNTH|   records {stats.RecordsIn}, extracted {stats.DocumentsExtracted}, out {stats.DocumentsOut}, text {product.Length} bytes, signatures {signatures.Length} bytes");
                text.AppendLine($"SYNTH|   languages: {string.Join(", ", stats.Languages.Select(language => $"{language.Name} {language.Count}"))}");
                text.AppendLine($"SYNTH|   drops: {string.Join(", ", stats.Drops.Select(drop => $"{drop.Name} {drop.Count}"))}");
                text.AppendLine($"SYNTH|   unsupported: {string.Join(", ", stats.UnsupportedLanguages.Select(language => $"{language.Name} {language.Count}"))}");
            }
        }

        TestContext.Progress.WriteLine(text.ToString());
    }

    #endregion
}
