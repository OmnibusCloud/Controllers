using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The rules of each language under a WITLID1 model (Corpus 0.1.14's rule data, now frozen as
/// data in <see cref="LanguageRuleSet.Witlid1"/>): the script decides what a word is (a letter,
/// for scripts without word spacing, except a borrowed Latin word), Japanese and Chinese carry
/// FineWeb-2's repetition thresholds, every language of the committed model has its stop words,
/// and the stop-word rule catches a page in another language under the label.
/// </summary>
[TestFixture]
public sealed class QualityRulesTests
{
    #region Constants

    private const string ARTICLE =
        "The committee reviewed the annual report in detail and approved the budget for the coming year.\n" +
        "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund.\n" +
        "The chair thanked the volunteers for their work on the summer programme and the autumn fair.\n" +
        "A vote on the revised bylaws was postponed until every member had received the final draft.\n" +
        "The next meeting will take place in the community hall on the first Tuesday of the month.";

    // Japanese thresholds without the stop-word rule (what 0.1.14 called QualityRules.Japanese).
    private static readonly QualityRules JAPANESE = LanguageRuleSet.Witlid1.For("jpn") with { MinStopWords = 0 };

    #endregion

    #region Script Tests

    [Test]
    public void UnspacedScriptsAreJudgedByTheirLettersTest()
    {
        const string chinese =
            "委员会详细审查了年度报告并批准了明年的预算。成员们就维护计划、人员配置和储备基金提出了问题。" +
            "主席感谢志愿者在夏季活动和秋季集市中的工作。关于修订章程的表决推迟到每位成员收到最终草案之后。" +
            "下次会议将于下月第一个星期二在社区礼堂举行。上次会议纪要经简短讨论后获得通过，没有修改。";

        var asSpaced = QualityFilter.Judge(chinese, QualityRules.Spaced);
        var asUnspaced = QualityFilter.Judge(chinese, LanguageRuleSet.Witlid1.For("zho"));

        Assert.Multiple(() =>
        {
            Assert.That(asSpaced.Passed, Is.False, "a space-split sees three giant words");
            Assert.That(asUnspaced.Passed, Is.True, string.Join(", ", asUnspaced.FailedRules));
            Assert.That(asUnspaced.Measures["words"], Is.GreaterThan(100));
            Assert.That(LanguageRuleSet.Witlid1.For("eng"), Is.SameAs(LanguageRuleSet.Witlid1.For("eng")));
            Assert.That(LanguageRuleSet.Witlid1.For(""), Is.SameAs(QualityRules.Spaced));
            Assert.That(LanguageRuleSet.Witlid1.For("yue"), Is.SameAs(QualityRules.Unspaced), "no stop-word list: the script's rules as they are");
            Assert.That(LanguageRuleSet.Witlid1.For("deu").MaxDuplicateNgramCharRatio, Is.SameAs(QualityRules.Spaced.MaxDuplicateNgramCharRatio));
            Assert.That(LanguageRuleSet.Witlid1.For("zho").CountsLetters, Is.True);
            Assert.That(LanguageRuleSet.Witlid1.For("zho").MaxDuplicateNgramCharRatio, Is.SameAs(QualityRules.Unspaced.MaxDuplicateNgramCharRatio));
            Assert.That(LanguageRuleSet.Witlid1.For("jpn").MaxDuplicateNgramCharRatio, Is.SameAs(JAPANESE.MaxDuplicateNgramCharRatio));
            Assert.That(LanguageRuleSet.Witlid1.For("jpn"), Is.SameAs(LanguageRuleSet.Witlid1.For("jpn")));
        });
    }

    [Test]
    public void ALatinWordInsideAnUnspacedScriptIsOneWordTest()
    {
        Assert.That(QualityFilter.Words("ESCOPYファイルを開く、Surface Neo。", QualityRules.Unspaced),
            Is.EqualTo(new[] { "ESCOPY", "フ", "ァ", "イ", "ル", "を", "開", "く", "Surface", "Neo" }));
    }

    [Test]
    public void ARepeatedLatinNameDoesNotReadAsRepeatedPhrasesTest()
    {
        // Six different Japanese sentences that each name the product: as one word, the name
        // repeats no 5-gram; spelled in letters of the script itself (katakana), every
        // occurrence after the first repeats the name's letters.
        string[] sentences =
        [
            "マイクロソフトは新しいSurfaceを発表した。", "このSurfaceは画面が二つある。", "価格は未定だが、Surfaceの予約は来月始まる。",
            "開発者はSurface向けのアプリを作れる。", "評価機を使った記者はSurfaceの重さに驚いた。", "発売日には店頭でSurfaceに触れられる。",
            "折りたたみ式の端末は各社から出ているが、二画面の機種はまだ少ない。", "電池の持ちについては発表がなかった。"
        ];
        var latin = string.Join('\n', sentences);
        var katakana = latin.Replace("Surface", "サーフェス", StringComparison.Ordinal);

        var asLatin = QualityFilter.Judge(latin, JAPANESE);
        var asKatakana = QualityFilter.Judge(katakana, JAPANESE);

        Assert.Multiple(() =>
        {
            Assert.That(asLatin.Measures["duplicate_ngram_chars_5"], Is.Zero);
            Assert.That(asKatakana.Measures["duplicate_ngram_chars_5"], Is.GreaterThan(0.1));
        });
    }

    [Test]
    public void JapaneseAndChineseCarryFineWeb2RepetitionThresholdsTest()
    {
        // A five-letter word said 22 times among 410 letters that never repeat: its repeats are
        // 105 of 520 characters (0.202), no longer n-gram repeats at all, the top 4-gram 0.169.
        // Gopher's 0.15 and FineWeb-2's Chinese 0.198 drop it, FineWeb-2's Japanese 0.243 keeps it.
        const string word = "甲乙丙丁戊";
        var builder = new System.Text.StringBuilder();
        var next = 0x4E00 + 0x100;
        for (var i = 0; i < 22; i++)
        {
            builder.Append(word);
            for (var j = 0; j < (i < 14 ? 19 : 18); j++)
                builder.Append((char)next++);
        }

        var text = builder.ToString();
        var asJapanese = QualityFilter.Judge(text, JAPANESE);
        var asChinese = QualityFilter.Judge(text, QualityRules.Unspaced);

        Assert.Multiple(() =>
        {
            Assert.That(text, Has.Length.EqualTo(520));
            Assert.That(asJapanese.Measures["duplicate_ngram_chars_5"], Is.EqualTo(105.0 / 520).Within(1e-12));
            Assert.That(asJapanese.Measures["duplicate_ngram_chars_6"], Is.Zero);
            Assert.That(asJapanese.Passed, Is.True, string.Join(", ", asJapanese.FailedRules));
            Assert.That(asChinese.FailedRules, Is.EqualTo(new[] { "duplicate_ngram_chars_5" }));
            Assert.That(JAPANESE.MaxDuplicateNgramCharRatio, Is.EqualTo(new[] { 0.243, 0.225, 0.207, 0.19, 0.175, 0.159 }));
            Assert.That(QualityRules.Unspaced.MaxDuplicateNgramCharRatio, Is.EqualTo(new[] { 0.198, 0.182, 0.167, 0.154, 0.14, 0.127 }));
        });
    }

    #endregion

    #region Stop Word Tests

    [Test]
    public void EveryModelLanguageHasStopWordsTest()
    {
        string[] model = ["ces", "deu", "eng", "fas", "fra", "ita", "jpn", "nld", "pol", "por", "rus", "spa", "tur", "ukr", "vie", "zho"];

        Assert.Multiple(() =>
        {
            foreach (var code in model)
            {
                Assert.That(LanguageRuleSet.Witlid1.For(code).MinStopWords, Is.EqualTo(2), code);
                Assert.That(LanguageRuleSet.Witlid1.For(code).StopWords, Is.Not.Empty, code);
            }

            Assert.That(LanguageRuleSet.Witlid1.For("deu").StopWords, Does.Contain("und"));
            Assert.That(LanguageRuleSet.Witlid1.For("jpn").StopWords, Does.Contain("の"));
        });
    }

    [Test]
    public void StopWordsCatchAPageInAnotherLanguageTest()
    {
        // Finnish, labelled Dutch (the committed model has no Finnish): none of FineWeb-2's Dutch
        // function words; a Dutch sentence of the same shape has several.
        const string finnish =
            "Kaupungin kirjasto avataan uudelleen ensi viikolla, kun remontti on valmis. Lukijat ovat odottaneet uusia tiloja koko kesän. " +
            "Lasten osasto siirtyy toiseen kerrokseen ja aikuisten lukusali saa uudet tuolit. Kahvila palvelee kävijöitä arkisin aamusta iltaan. " +
            "Kirjaston johtaja kiittää kaupunkilaisia kärsivällisyydestä ja lupaa monipuolista ohjelmaa syksyksi. Avajaisissa esiintyy paikallinen kuoro. " +
            "Uusi verkkopalvelu kertoo lainojen eräpäivät ja varausten tilanteen. Kirjastokortin voi uusia paikan päällä ilman maksua.";
        const string dutch =
            "De bibliotheek van de stad gaat volgende week weer open, nu de verbouwing klaar is. Lezers hebben de hele zomer op de nieuwe zalen gewacht. " +
            "De kinderafdeling verhuist naar de tweede verdieping en de leeszaal voor volwassenen krijgt nieuwe stoelen. Het café is op werkdagen van de ochtend tot de avond open. " +
            "De directeur bedankt de inwoners voor hun geduld en belooft een gevarieerd programma voor het najaar. Bij de opening zingt een plaatselijk koor.";

        Assert.Multiple(() =>
        {
            Assert.That(QualityFilter.Judge(finnish, LanguageRuleSet.Witlid1.For("nld")).FailedRules, Is.EqualTo(new[] { "stop_words" }));
            Assert.That(QualityFilter.Judge(dutch, LanguageRuleSet.Witlid1.For("nld")).Passed, Is.True);
        });
    }

    [Test]
    public void EnglishNeedsTwoStopWordsTest()
    {
        // Gopher's stop-word filter, English only: a page in a language the identifier does not
        // know (Indonesian here), labelled English, has none of the eight words.
        var indonesian = "Pemerintah daerah menyiapkan cadangan beras untuk musim panen raya tahun ini di beberapa provinsi. "
            + string.Join(' ', Enumerable.Range(0, 60).Select(i => $"kata{i}"));

        var article = QualityFilter.Judge(ARTICLE, LanguageRuleSet.Witlid1.For("eng"));
        var foreign = QualityFilter.Judge(indonesian, LanguageRuleSet.Witlid1.For("eng"));

        Assert.Multiple(() =>
        {
            Assert.That(article.Passed, Is.True, string.Join(", ", article.FailedRules));
            Assert.That(article.Measures["stop_words"], Is.GreaterThanOrEqualTo(2));
            Assert.That(foreign.FailedRules, Does.Contain("stop_words"));
            Assert.That(QualityFilter.Judge(indonesian, QualityRules.Spaced).FailedRules, Does.Not.Contain("stop_words"), "only English has the rule");
        });
    }

    #endregion
}
