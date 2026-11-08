using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// Language identification on the golden WET shard with the committed model (16 languages,
/// trained by corpus-assets on two CC-MAIN-2026-43 WET files, 31k documents labelled by
/// Common Crawl's classifier): the model pins to its hash, agrees with Common Crawl's labels on
/// the shard well above 95 %, answers the same every time, says nothing about too little
/// text, and a training run is a deterministic function of its input.
/// </summary>
[TestFixture]
public sealed class LanguageIdentifierTests
{
    #region Constants

    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";
    private const string MODEL_SHA256 = "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f";
    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";

    #endregion

    #region Model Tests

    [Test]
    public void CommittedModelPinsToItsHashAndRoundTripsTest()
    {
        var bytes = File.ReadAllBytes(Fixture(MODEL_FIXTURE));
        var model = LanguageModel.FromBytes(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(model.Sha256(), Is.EqualTo(MODEL_SHA256));
            Assert.That(model.ToBytes(), Is.EqualTo(bytes), "serialization is the identity on its own output");
            Assert.That(model.Profiles, Has.Count.EqualTo(16));
            Assert.That(model.Profiles.Select(p => p.Code), Is.Ordered.Ascending.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(model.Profiles.All(p => p.Grams.Count == LanguageModelTrainer.DEFAULT_PROFILE_SIZE), Is.True);
            Assert.That(model.Profiles.Select(p => p.Code), Does.Contain("eng").And.Contain("rus").And.Contain("zho").And.Contain("jpn"));
        });
    }

    [Test]
    public void EveryLanguageProfileRanksItsOwnSampleFirstTest()
    {
        var model = LanguageModel.Load(Fixture(MODEL_FIXTURE));
        var identifier = new LanguageIdentifier(model);
        var samples = new Dictionary<string, string>
        {
            ["eng"] = "The quick brown fox jumps over the lazy dog while the committee reviews the annual report in detail.",
            ["deu"] = "Der schnelle braune Fuchs springt über den faulen Hund, während der Ausschuss den Jahresbericht prüft.",
            ["fra"] = "Le rapide renard brun saute par-dessus le chien paresseux pendant que le comité examine le rapport annuel.",
            ["rus"] = "Быстрая коричневая лиса перепрыгивает через ленивую собаку, пока комитет рассматривает годовой отчёт.",
            ["ukr"] = "Швидка руда лисиця перестрибує через ледачого пса, поки комітет розглядає річний звіт за минулий рік.",
            ["spa"] = "El rápido zorro marrón salta sobre el perro perezoso mientras el comité revisa el informe anual.",
            ["por"] = "A rápida raposa marrom salta sobre o cão preguiçoso enquanto o comitê analisa o relatório anual.",
            ["ita"] = "La veloce volpe marrone salta sopra il cane pigro mentre il comitato esamina la relazione annuale.",
            ["pol"] = "Szybki brązowy lis przeskakuje nad leniwym psem, podczas gdy komisja analizuje roczne sprawozdanie.",
            ["ces"] = "Rychlá hnědá liška skáče přes líného psa, zatímco výbor podrobně posuzuje výroční zprávu.",
            ["nld"] = "De snelle bruine vos springt over de luie hond terwijl de commissie het jaarverslag bestudeert.",
            ["tur"] = "Hızlı kahverengi tilki tembel köpeğin üzerinden atlarken komite yıllık raporu ayrıntılı inceliyor.",
            ["vie"] = "Con cáo nâu nhanh nhẹn nhảy qua con chó lười trong khi ủy ban xem xét báo cáo thường niên chi tiết.",
            ["fas"] = "روباه قهوه‌ای سریع از روی سگ تنبل می‌پرد در حالی که کمیته گزارش سالانه را بررسی می‌کند.",
            ["jpn"] = "素早い茶色の狐が怠け者の犬を飛び越える間、委員会は年次報告書を詳しく検討している。",
            ["zho"] = "敏捷的棕色狐狸跳过懒狗，与此同时委员会正在详细审查年度报告的每一个部分。"
        };

        var wrong = samples
            .Select(pair => (Expected: pair.Key, Guess: identifier.Identify(pair.Value)))
            .Where(result => result.Guess.Code != result.Expected)
            .Select(result => $"{result.Expected} -> {result.Guess.Code} (runner-up {result.Guess.RunnerUp}, margin {result.Guess.Margin:F3})")
            .ToList();

        Assert.That(wrong, Is.Empty, string.Join("; ", wrong));
    }

    [Test]
    public void AgreesWithCommonCrawlsLabelsOnTheShardTest()
    {
        var model = LanguageModel.Load(Fixture(MODEL_FIXTURE));
        var identifier = new LanguageIdentifier(model);
        var known = model.Profiles.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

        var total = 0;
        var agree = 0;
        var disagreements = new List<string>();
        using var reader = WarcReader.Open(Fixture(WET_FIXTURE));
        foreach (var record in reader.ReadRecords())
        {
            var label = record.IdentifiedLanguage;
            if (record.Type != "conversion" || string.IsNullOrEmpty(label) || label.Contains(',') || !known.Contains(label))
                continue;

            total++;
            var guess = identifier.Identify(Encoding.UTF8.GetString(record.Payload.Span));
            if (guess.Code == label)
                agree++;
            else
                disagreements.Add($"{label}->{(guess.IsKnown ? guess.Code : "?")}");
        }

        TestContext.Out.WriteLine($"{total} labelled documents: agree {agree} ({100.0 * agree / total:F1} %); disagreements: {string.Join(" ", disagreements)}");

        Assert.That(total, Is.GreaterThan(200));
        Assert.That((double)agree / total, Is.GreaterThan(0.95));
    }

    [Test]
    public void IdentificationIsDeterministicAndShortTextIsUnknownTest()
    {
        var identifier = new LanguageIdentifier(LanguageModel.Load(Fixture(MODEL_FIXTURE)));
        const string text = "Die Kommission hat den Bericht angenommen und die nächsten Schritte beschlossen.";

        var first = identifier.Identify(text);
        var second = identifier.Identify(text);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Code, Is.EqualTo("deu"));
            Assert.That(identifier.Identify("ok").IsKnown, Is.False);
            Assert.That(identifier.Identify("12345 67890 !!! ???").IsKnown, Is.False);
            Assert.That(identifier.Identify("").IsKnown, Is.False);
        });
    }

    [Test]
    public void TrainingIsAFunctionOfItsInputTest()
    {
        static byte[] Train()
        {
            var trainer = new LanguageModelTrainer(profileSize: 500, minDocuments: 5);
            trainer.AddWet(Fixture(WET_FIXTURE));
            return trainer.Build().ToBytes();
        }

        var one = Train();
        var two = Train();
        var model = LanguageModel.FromBytes(one);

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(model.Profiles, Is.Not.Empty);
            Assert.That(model.Profiles.Select(p => p.Code), Does.Contain("eng"));
        });
    }

    [Test]
    public void NgramsAreCaseAndPunctuationInsensitiveTest()
    {
        var a = new Dictionary<string, long>(StringComparer.Ordinal);
        var b = new Dictionary<string, long>(StringComparer.Ordinal);
        LanguageNgrams.Count("Hello, World!", a);
        LanguageNgrams.Count("hello world", b);

        Assert.That(a, Is.EquivalentTo(b));
        Assert.That(a.Keys, Does.Contain("_hel").And.Contain("llo_").And.Contain("w"));
        Assert.That(a.Keys, Does.Not.Contain("_"));
    }

    #endregion

    #region Helpers

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
