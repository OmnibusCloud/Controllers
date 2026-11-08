using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// A diagnostic for tuning the thresholds (Explicit: run by hand): over the golden WET shard,
/// which rule fires first per document, how many documents each rule alone would drop, and
/// a few samples of the documents the heaviest rule drops, so a threshold change is argued
/// from text rather than from a count.
/// </summary>
[TestFixture]
[Explicit("Tuning diagnostic: prints the drop profile of the golden WET shard")]
public sealed class QualityDropDiagnosticsTests
{
    [Test]
    public void PrintDropProfileTest()
    {
        var model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-model-cc-2026-43-16.bin"));
        var identifier = new LanguageIdentifier(model);
        using var reader = WarcReader.Open(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cc-main-2026-43-00000-head.warc.wet.gz"));

        var firstRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var anyRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var onlyRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<(string Uri, string Text, IReadOnlyDictionary<string, double> Measures)>();
        int documents = 0, passed = 0;

        foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
        {
            var text = HtmlTextExtractor.Collapse(Encoding.UTF8.GetString(record.Payload.Span));
            var guess = identifier.Identify(text);
            if (!guess.IsKnown)
                continue;

            documents++;
            var verdict = QualityFilter.Judge(text, QualityRules.ForLanguage(guess.Code));
            if (verdict.Passed)
            {
                passed++;
                continue;
            }

            firstRule[verdict.FailedRules[0]] = firstRule.GetValueOrDefault(verdict.FailedRules[0]) + 1;
            foreach (var rule in verdict.FailedRules)
                anyRule[rule] = anyRule.GetValueOrDefault(rule) + 1;
            if (verdict.FailedRules.Count == 1)
                onlyRule[verdict.FailedRules[0]] = onlyRule.GetValueOrDefault(verdict.FailedRules[0]) + 1;

            if (verdict.FailedRules[0] == "duplicate_ngram_chars_5" && verdict.FailedRules.Count == 1 && samples.Count < 4)
                samples.Add((record.TargetUri ?? "", text, verdict.Measures));
        }

        TestContext.Out.WriteLine($"documents with a known language: {documents}, passed: {passed} ({100.0 * passed / documents:F1} %)");
        TestContext.Out.WriteLine("first failing rule:   " + string.Join(", ", firstRule.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        TestContext.Out.WriteLine("fails this rule:      " + string.Join(", ", anyRule.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        TestContext.Out.WriteLine("fails ONLY this rule: " + string.Join(", ", onlyRule.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));

        foreach (var (uri, text, measures) in samples)
        {
            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine($"--- {uri}  (measures: {string.Join(", ", measures.Where(m => m.Key.Contains("ngram")).Select(m => $"{m.Key}={m.Value:F3}"))})");
            TestContext.Out.WriteLine(text.Length > 700 ? text[..700] + " …" : text);
        }

        Assert.That(documents, Is.GreaterThan(0));
    }
}
