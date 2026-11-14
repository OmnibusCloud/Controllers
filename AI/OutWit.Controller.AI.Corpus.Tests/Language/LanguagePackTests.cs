using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// The committed reference pack (WITLID2, built by <c>corpus-assets build-language-pack</c>): it
/// pins to its hash; its 16 supported profiles are the 0.1.x model's, so it never labels a
/// document with another language than that model did, only unknown; its rule data is the
/// rule data 0.1.14 had in code; and the unsupported-language test turns text in languages the
/// pack does not support unknown — Scandinavian, Catalan, Slovak, Romanian and the others the
/// tuning found mislabelled — while text in the supported languages keeps its label. The samples
/// are short sentences written for these tests.
/// </summary>
[TestFixture]
public sealed class LanguagePackTests
{
    #region Constants

    private const string PACK_FIXTURE = "language-pack-cc-2026-43-16.bin";
    private const string WITLID1_FIXTURE = "language-model-cc-2026-43-16.bin";

    /// <summary>One paragraph per supported language (the same as the WITLID1 model's tests).</summary>
    private static readonly Dictionary<string, string> SUPPORTED = new()
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

    /// <summary>One paragraph per language the pack does not support; the first ones have a known profile in the pack, the last ones none.</summary>
    private static readonly Dictionary<string, string> UNSUPPORTED = new()
    {
        ["swe"] = "Kommunen öppnar det nya biblioteket nästa vecka, när renoveringen är klar. Barnavdelningen flyttar till andra våningen och läsesalen får nya stolar. Kaféet har öppet varje dag från morgon till kväll.",
        ["dan"] = "Kommunen åbner det nye bibliotek i næste uge, når renoveringen er færdig. Børneafdelingen flytter til anden sal, og læsesalen får nye stole. Caféen har åbent hver dag fra morgen til aften.",
        ["nor"] = "Kommunen åpner det nye biblioteket neste uke, når oppussingen er ferdig. Barneavdelingen flytter til andre etasje, og lesesalen får nye stoler. Kafeen har åpent hver dag fra morgen til kveld.",
        ["cat"] = "L'ajuntament obrirà la nova biblioteca la setmana que ve, quan s'acabin les obres. La secció infantil es trasllada al segon pis i la sala de lectura tindrà cadires noves. La cafeteria obre cada dia des del matí fins al vespre.",
        ["slk"] = "Mesto otvorí novú knižnicu budúci týždeň, keď sa skončí rekonštrukcia. Detské oddelenie sa presťahuje na druhé poschodie a čitáreň dostane nové stoličky. Kaviareň je otvorená každý deň od rána do večera.",
        ["ron"] = "Primăria va deschide noua bibliotecă săptămâna viitoare, după ce se termină renovarea. Secția pentru copii se mută la etajul al doilea, iar sala de lectură primește scaune noi. Cafeneaua este deschisă în fiecare zi, de dimineața până seara.",
        ["hun"] = "A város jövő héten nyitja meg az új könyvtárat, amikor befejeződik a felújítás. A gyermekrészleg a második emeletre költözik, az olvasóterem pedig új székeket kap. A kávézó minden nap nyitva van reggeltől estig.",
        ["fin"] = "Kaupunki avaa uuden kirjaston ensi viikolla, kun remontti on valmis. Lasten osasto siirtyy toiseen kerrokseen ja lukusali saa uudet tuolit. Kahvila on auki joka päivä aamusta iltaan.",
        ["ind"] = "Pemerintah kota akan membuka perpustakaan baru minggu depan setelah renovasi selesai. Bagian anak-anak pindah ke lantai dua dan ruang baca mendapat kursi baru. Kafe buka setiap hari dari pagi sampai malam.",
        ["bul"] = "Общината ще открие новата библиотека следващата седмица, когато ремонтът приключи. Детският отдел се мести на втория етаж, а читалнята получава нови столове. Кафенето е отворено всеки ден от сутрин до вечер.",
        ["hrv"] = "Grad će sljedeći tjedan otvoriti novu knjižnicu, kada se završi obnova. Dječji odjel seli se na drugi kat, a čitaonica dobiva nove stolice. Kafić je otvoren svaki dan od jutra do večeri.",
        ["ell"] = "Ο δήμος θα ανοίξει τη νέα βιβλιοθήκη την επόμενη εβδομάδα, όταν τελειώσει η ανακαίνιση. Το παιδικό τμήμα μεταφέρεται στον δεύτερο όροφο και το αναγνωστήριο παίρνει καινούργιες καρέκλες.",
        ["kor"] = "시는 리모델링이 끝나면 다음 주에 새 도서관을 엽니다. 어린이 코너는 2층으로 옮기고 열람실에는 새 의자가 들어옵니다. 카페는 매일 아침부터 저녁까지 문을 엽니다.",
        ["eus"] = "Udalak liburutegi berria irekiko du datorren astean, berritze lanak amaitzen direnean. Haurren saila bigarren solairura joango da, eta irakurgelak aulki berriak izango ditu. Kafetegia egunero dago irekita goizetik arratsaldera.",
        ["sqi"] = "Bashkia do të hapë bibliotekën e re javën e ardhshme, kur të përfundojë rinovimi. Seksioni i fëmijëve zhvendoset në katin e dytë dhe salla e leximit merr karrige të reja. Kafeneja është e hapur çdo ditë nga mëngjesi deri në mbrëmje.",
        ["cym"] = "Bydd y cyngor yn agor y llyfrgell newydd yr wythnos nesaf, pan fydd y gwaith adnewyddu wedi gorffen. Bydd adran y plant yn symud i'r ail lawr a bydd yr ystafell ddarllen yn cael cadeiriau newydd."
    };

    #endregion

    #region Fields

    private LanguageModel m_pack = null!;
    private LanguageModel m_witlid1 = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_pack = LanguageModel.Load(Fixture(PACK_FIXTURE));
        m_witlid1 = LanguageModel.Load(Fixture(WITLID1_FIXTURE));
    }

    #endregion

    #region Pack Tests

    [Test]
    public void ThePackPinsToItsHashAndRoundTripsTest()
    {
        var bytes = File.ReadAllBytes(Fixture(PACK_FIXTURE));

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(m_pack.Sha256(), Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(m_pack.ToBytes(), Is.EqualTo(bytes));
            Assert.That(m_pack.Format, Is.EqualTo(CorpusReferenceLanguageModel.FORMAT));
            Assert.That(m_pack.Name, Is.EqualTo(CorpusReferenceLanguageModel.NAME));
            Assert.That(m_pack.Languages, Is.EqualTo(CorpusReferenceLanguageModel.LANGUAGES));
            Assert.That(m_pack.Profiles.Select(profile => profile.Code).Except(m_pack.Languages), Is.EqualTo(CorpusReferenceLanguageModel.KNOWN_LANGUAGES));
            Assert.That(m_pack.Languages.All(code => m_pack.Acceptance!.MaxDistancePpm(code) > 0), Is.True, "every supported language has a calibrated limit");
            Assert.That(m_pack.Metadata["recall"], Is.EqualTo("0.999"));
            Assert.That(m_pack.Metadata["training"], Does.Contain("CC-MAIN-20261005105623-20261005135623-00001.warc.wet.gz sha256:c64f5d23b7b84031f72e5d9f8b2ed7edcfe7596262e241c91e2db789a0fd149c"));
            Assert.That(m_pack.Metadata["calibration"], Does.Contain("CC-MAIN-20261005105623-20261005135623-00030.warc.wet.gz"));
        });
    }

    [Test]
    public void TheSupportedProfilesAreThoseOfTheWitlid1ModelTest()
    {
        // The pack's supported profiles are the 0.1.x model's to the n-gram, so a document the
        // pack labels gets exactly the label that model gave it: the pack only adds "unknown".
        var witlid1 = m_witlid1.Profiles.ToDictionary(profile => profile.Code);

        Assert.Multiple(() =>
        {
            Assert.That(m_witlid1.Languages, Is.EqualTo(m_pack.Languages));
            foreach (var code in m_pack.Languages)
                Assert.That(m_pack.Profiles.Single(profile => profile.Code == code).Grams, Is.EqualTo(witlid1[code].Grams), code);
        });
    }

    [Test]
    public void TheRuleDataIsTheRuleDataOf0114Test()
    {
        Assert.Multiple(() =>
        {
            foreach (var code in m_pack.Languages)
                Assert.That(Describe(m_pack.Rules.For(code)), Is.EqualTo(Describe(LanguageRuleSet.Witlid1.For(code))), code);
        });
    }

    #endregion

    #region Unsupported Language Tests

    [Test]
    public void EverySupportedSampleKeepsItsLabelTest()
    {
        var identifier = new LanguageIdentifier(m_pack);
        var wrong = SUPPORTED
            .Select(pair => (Expected: pair.Key, Guess: identifier.Identify(pair.Value)))
            .Where(result => result.Guess.Code != result.Expected)
            .Select(result => $"{result.Expected} -> '{result.Guess.Code}' (nearest {result.Guess.Nearest}, {result.Guess.DistancePpm} ppm, limit {m_pack.Acceptance!.MaxDistancePpm(result.Guess.Nearest)})")
            .ToList();

        Assert.That(wrong, Is.Empty, string.Join("; ", wrong));
    }

    [Test]
    public void EveryUnsupportedSampleIsUnknownTest()
    {
        var identifier = new LanguageIdentifier(m_pack);
        var labelled = UNSUPPORTED
            .Select(pair => (Language: pair.Key, Guess: identifier.Identify(pair.Value)))
            .Where(result => result.Guess.IsKnown || !result.Guess.IsUnsupported)
            .Select(result => $"{result.Language} -> '{result.Guess.Code}' (nearest {result.Guess.Nearest}, {result.Guess.DistancePpm} ppm)")
            .ToList();

        Assert.That(labelled, Is.Empty, string.Join("; ", labelled));
    }

    [Test]
    public void AKnownLanguageFindsItsOwnProfileTest()
    {
        var identifier = new LanguageIdentifier(m_pack);

        Assert.Multiple(() =>
        {
            foreach (var code in new[] { "swe", "dan", "nor", "cat", "slk" })
            {
                var guess = identifier.Identify(UNSUPPORTED[code]);
                Assert.That(guess.Nearest, Is.EqualTo(code), code);
                Assert.That(guess.IsUnsupported, Is.True, code);
            }
        });
    }

    [Test]
    public void TheWitlid1ModelLabelsTheSameSamplesWithTheNearestLanguageTest()
    {
        // What 0.1.14 does with the same text: no unsupported verdict, a supported label.
        var identifier = new LanguageIdentifier(m_witlid1);

        Assert.Multiple(() =>
        {
            foreach (var (language, text) in UNSUPPORTED)
            {
                var guess = identifier.Identify(text);
                Assert.That(guess.IsKnown, Is.True, language);
                Assert.That(guess.IsUnsupported, Is.False, language);
            }
        });
    }

    [Test]
    public void TooLittleTextIsUnknownNotUnsupportedTest()
    {
        var identifier = new LanguageIdentifier(m_pack);

        Assert.Multiple(() =>
        {
            foreach (var text in new[] { "ok", "12345 67890 !!! ???", "", "Hej då" })
            {
                var guess = identifier.Identify(text);
                Assert.That(guess.IsKnown, Is.False, text);
                Assert.That(guess.IsUnsupported, Is.False, text);
            }
        });
    }

    [Test]
    public void IdentificationWithThePackIsDeterministicTest()
    {
        var identifier = new LanguageIdentifier(m_pack);

        Assert.Multiple(() =>
        {
            foreach (var text in SUPPORTED.Values.Concat(UNSUPPORTED.Values))
                Assert.That(identifier.Identify(text), Is.EqualTo(identifier.Identify(text)));
        });
    }

    #endregion

    #region Tools

    private static string Describe(QualityRules rules)
    {
        return string.Join("|",
            rules.CountsLetters, rules.MinWords, rules.MaxWords, rules.MinMeanWordLength, rules.MaxMeanWordLength, rules.MaxSymbolToWordRatio,
            rules.MaxBulletLineRatio, rules.MaxEllipsisLineRatio, rules.MinAlphabeticWordRatio, rules.MaxDuplicateLineRatio, rules.MaxDuplicateLineCharRatio,
            string.Join(",", rules.MaxTopNgramCharRatio), string.Join(",", rules.MaxDuplicateNgramCharRatio),
            string.Join(",", rules.StopWords.Order(StringComparer.Ordinal)), rules.MinStopWords, rules.FilterCurlyBracket);
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
