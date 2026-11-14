using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// What <c>corpus-assets build-language-pack</c> and <c>evaluate-language-pack</c> do, on a tiny
/// fixture written at test time: labelled WET files in three made-up word lists (English and
/// German supported through a source folder, Swedish only labelled). The build trains the
/// supported profiles as a WITLID1 model would from the same files, makes Swedish a known
/// unsupported profile, calibrates a limit per supported language, records how it was built,
/// and gives the same bytes twice; the evaluation counts recall, rejection and the confusion.
/// </summary>
[TestFixture]
public sealed class LanguagePackBuilderTests
{
    #region Constants

    private const int PROFILE_SIZE = 300;
    private const int MIN_DOCUMENTS = 20;

    private static readonly string[] ENGLISH = ["the", "library", "opens", "next", "week", "when", "work", "is", "done", "children", "move", "to", "second", "floor", "and", "reading", "room", "gets", "new", "chairs", "cafe", "every", "day", "from", "morning", "until", "evening", "city", "school", "water", "street", "over", "also", "had", "would", "some", "itself"];
    private static readonly string[] GERMAN = ["der", "die", "und", "nicht", "mit", "auf", "für", "ist", "eine", "werden", "zwischen", "stadt", "bibliothek", "woche", "kinder", "abteilung", "geöffnet", "morgen", "abend", "neue", "stühle", "lesesaal", "umbau", "fertig", "zweiten", "stock", "jeden", "tag", "bis", "vom", "rathaus", "bürger", "schule", "wasser", "straße", "über", "dass", "wurde"];
    private static readonly string[] SWEDISH = ["och", "det", "att", "som", "en", "på", "är", "för", "med", "inte", "kommunen", "biblioteket", "veckan", "barnen", "avdelningen", "öppet", "morgon", "kväll", "nya", "stolar", "läsesalen", "renoveringen", "klar", "andra", "våningen", "varje", "dag", "till", "från", "staden", "skolan", "vatten", "gatan", "över", "också", "hade", "skulle", "några", "själv"];

    #endregion

    #region Fields

    private string m_directory = null!;
    private string m_languages = null!;
    private string m_train = null!;
    private string m_calibrate = null!;
    private string m_evaluate = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), "corpus-language-pack-" + Guid.NewGuid().ToString("N"));
        m_languages = Path.Combine(m_directory, "languages");
        Write("languages/eng/language.json", """{ "name": "English", "script": "Latn" }""");
        Write("languages/eng/stopwords.txt", "# made up for the test\nthe\nand\nto\n");
        Write("languages/deu/language.json", """{ "name": "German", "script": "Latn", "quality": { "min_words": 40 } }""");
        Write("languages/deu/stopwords.txt", "der\ndie\nund\n");

        m_train = Shard("train.warc.wet.gz", seed: 1, perLanguage: 30);
        m_calibrate = Shard("calibrate.warc.wet.gz", seed: 2, perLanguage: 60);
        m_evaluate = Shard("evaluate.warc.wet.gz", seed: 3, perLanguage: 40);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Build Tests

    [Test]
    public void ThePackHasTheSupportedAndTheKnownLanguagesTest()
    {
        var pack = Build();

        Assert.Multiple(() =>
        {
            Assert.That(pack.Format, Is.EqualTo(LanguageModelFormat.WITLID2));
            Assert.That(pack.Name, Is.EqualTo("tiny"));
            Assert.That(pack.Languages, Is.EqualTo(new[] { "deu", "eng" }));
            Assert.That(pack.Profiles.Select(profile => profile.Code), Is.EqualTo(new[] { "deu", "eng", "swe" }), "Swedish is known, not supported");
            Assert.That(pack.Languages.All(code => pack.Acceptance!.MaxDistancePpm(code) > 0), Is.True, "every supported language is calibrated");
            Assert.That(pack.Rules.For("deu").MinWords, Is.EqualTo(40), "the source folder's threshold");
            Assert.That(pack.Rules.For("eng").StopWords, Is.EquivalentTo(new[] { "the", "and", "to" }));
            Assert.That(pack.Metadata["training"], Does.StartWith("train.warc.wet.gz sha256:"));
            Assert.That(pack.Metadata["calibration"], Does.StartWith("calibrate.warc.wet.gz sha256:"));
            Assert.That(pack.Metadata["recall"], Is.EqualTo("0.999"));
            Assert.That(pack.Metadata["training_documents"], Is.EqualTo("deu=30 eng=30"));
            Assert.That(pack.Metadata["known_documents"], Is.EqualTo("swe=30"));
        });
    }

    [Test]
    public void TheSupportedProfilesAreTheWitlid1TrainersTest()
    {
        var pack = Build();
        var trainer = new LanguageModelTrainer(PROFILE_SIZE, MIN_DOCUMENTS);
        trainer.AddWet(m_train);
        var witlid1 = trainer.Build().Profiles.ToDictionary(profile => profile.Code);

        Assert.Multiple(() =>
        {
            foreach (var code in pack.Languages)
                Assert.That(pack.Profiles.Single(profile => profile.Code == code).Grams, Is.EqualTo(witlid1[code].Grams), code);
        });
    }

    [Test]
    public void TheBuildIsAFunctionOfItsInputTest()
    {
        var one = Build().ToBytes();
        var two = Build().ToBytes();

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.EqualTo(one));
            Assert.That(LanguageModel.FromBytes(one).ToBytes(), Is.EqualTo(one));
        });
    }

    [Test]
    public void TooFewDocumentsAreAnErrorNotAWeakPackTest()
    {
        var training = new LanguagePackBuilder(LanguagePackSource.Read(m_languages), PROFILE_SIZE, minDocuments: 31, knownMinDocuments: MIN_DOCUMENTS);
        training.AddTrainingWet(m_train);
        Assert.That(() => training.Build("tiny"), Throws.InvalidOperationException.With.Message.Contains("deu has 30"));

        var calibrator = new LanguagePackCalibrator(Uncalibrated());
        calibrator.AddWet(m_train);
        Assert.That(() => calibrator.Limits(LanguagePackCalibrator.DEFAULT_RECALL_PPM), Throws.InvalidOperationException.With.Message.Contains("eng has 30"));
    }

    [Test]
    public void TheLimitIsTheSampleAtTheTargetRecallTest()
    {
        var calibrator = new LanguagePackCalibrator(Uncalibrated());
        calibrator.AddWet(m_calibrate);

        var all = calibrator.Limits(LanguageIdentifier.PPM);
        var most = calibrator.Limits(500_000);

        Assert.Multiple(() =>
        {
            Assert.That(calibrator.Documents["eng"], Is.EqualTo(60));
            Assert.That(most["eng"], Is.LessThanOrEqualTo(all["eng"]), "a lower recall is a tighter limit");
            Assert.That(() => calibrator.Limits(0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    #endregion

    #region Evaluation Tests

    [Test]
    public void TheEvaluationCountsRecallRejectionAndConfusionTest()
    {
        var evaluator = new LanguagePackEvaluator(Build(), judge: true);
        evaluator.AddWet(m_evaluate);

        var english = evaluator.Labels["eng"];
        var swedish = evaluator.Labels["swe"];

        Assert.Multiple(() =>
        {
            Assert.That(english.Documents, Is.EqualTo(40));
            Assert.That(english.LabelledAs("eng"), Is.GreaterThanOrEqualTo(38), "supported text keeps its label");
            Assert.That(english.NearestIs("eng"), Is.EqualTo(40), "without the test, the nearest supported language");
            Assert.That(swedish.Documents, Is.EqualTo(40));
            Assert.That(swedish.Unsupported, Is.EqualTo(40), "text in a known unsupported language is unknown");
            Assert.That(swedish.Nearest.Values.Sum(), Is.EqualTo(40), "and a model without the test would have labelled it all");
            Assert.That(evaluator.Labels[LanguagePackEvaluator.MIXED_SUPPORTED].Documents, Is.EqualTo(5));
        });
    }

    [Test]
    public void AWitlid1ModelEvaluatesWithoutAnUnsupportedVerdictTest()
    {
        var trainer = new LanguageModelTrainer(PROFILE_SIZE, MIN_DOCUMENTS);
        trainer.AddWet(m_train);
        var model = trainer.Build();
        var evaluator = new LanguagePackEvaluator(model, judge: false);
        evaluator.AddWet(m_evaluate);

        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Labels.Values.Sum(tally => tally.Unsupported), Is.Zero);
            Assert.That(evaluator.Labels["swe"].LabelledAs("swe"), Is.EqualTo(40), "a WITLID1 model trained on Swedish supports it");
        });
    }

    #endregion

    #region Tools

    private LanguageModel Uncalibrated()
    {
        var builder = new LanguagePackBuilder(LanguagePackSource.Read(m_languages), PROFILE_SIZE, MIN_DOCUMENTS, MIN_DOCUMENTS);
        builder.AddTrainingWet(m_train);
        return builder.Build("tiny");
    }

    private LanguageModel Build()
    {
        var calibrator = new LanguagePackCalibrator(Uncalibrated());
        calibrator.AddWet(m_calibrate);
        return calibrator.Apply(LanguagePackCalibrator.DEFAULT_RECALL_PPM);
    }

    private string Shard(string name, int seed, int perLanguage)
    {
        var random = new Random(seed);
        var records = new List<byte[]>();
        for (var i = 0; i < perLanguage; i++)
        {
            records.Add(WarcSynth.LabelledConversionRecord($"https://example.test/eng/{seed}/{i}", Text(random, ENGLISH), "eng"));
            records.Add(WarcSynth.LabelledConversionRecord($"https://example.test/deu/{seed}/{i}", Text(random, GERMAN), "deu"));
            records.Add(WarcSynth.LabelledConversionRecord($"https://example.test/swe/{seed}/{i}", Text(random, SWEDISH), "swe"));
        }

        for (var i = 0; i < 5; i++)
            records.Add(WarcSynth.LabelledConversionRecord($"https://example.test/mixed/{seed}/{i}", Text(random, ENGLISH) + "\n" + Text(random, GERMAN), "eng,deu"));

        var path = Path.Combine(m_directory, name);
        File.WriteAllBytes(path, WarcSynth.Shard(records));
        return path;
    }

    private static string Text(Random random, string[] words)
    {
        var builder = new StringBuilder();
        for (var sentence = 0; sentence < 8; sentence++)
        {
            for (var i = 0; i < 9; i++)
                builder.Append(i == 0 ? "" : " ").Append(words[random.Next(words.Length)]);
            builder.Append(".\n");
        }

        return builder.ToString();
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(m_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    #endregion
}
