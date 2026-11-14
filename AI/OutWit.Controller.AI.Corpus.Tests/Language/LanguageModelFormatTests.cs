using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// The two model formats: a WITLID2 pack round-trips to the same bytes with every part intact
/// (profiles, supported and known languages, rule data, limits, metadata); a WITLID1 model still
/// reads and writes exactly as before; a pack that is not canonical, not complete or not for
/// this controller is refused, never half-read.
/// </summary>
[TestFixture]
public sealed class LanguageModelFormatTests
{
    #region Constants

    private const string WITLID1_FIXTURE = "language-model-cc-2026-43-16.bin";
    private const string WITLID1_SHA256 = "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f";

    #endregion

    #region Round Trip Tests

    [Test]
    public void APackRoundTripsWithEveryPartTest()
    {
        var pack = SmallPack();
        var bytes = pack.ToBytes();
        var read = LanguageModel.FromBytes(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(read.ToBytes(), Is.EqualTo(bytes), "reading and writing again gives the same bytes");
            Assert.That(read.Sha256(), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(read.Format, Is.EqualTo(LanguageModelFormat.WITLID2));
            Assert.That(read.Name, Is.EqualTo("tiny"));
            Assert.That(read.Profiles.Select(profile => profile.Code), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(read.Profiles.Select(profile => profile.Grams), Is.EqualTo(pack.Profiles.Select(profile => profile.Grams)));
            Assert.That(read.Languages, Is.EqualTo(new[] { "aaa", "bbb" }), "ccc is known, not supported");
            Assert.That(read.Acceptance!.MaxDistancePpm("aaa"), Is.EqualTo(400_000));
            Assert.That(read.Acceptance.MaxDistancePpm("bbb"), Is.Zero, "no limit");
            Assert.That(read.Acceptance.MaxDistancePpm("ccc"), Is.EqualTo(-1), "not supported");
            Assert.That(read.Metadata, Is.EqualTo(pack.Metadata));
            Assert.That(read.Rules.Languages.Zip(pack.Rules.Languages).All(pair => pair.First.SameAs(pair.Second)), Is.True);
            Assert.That(read.Rules.For("bbb").MaxTopNgramCharRatio, Is.EqualTo(new[] { 0.3, 0.2, 0.1 }));
            Assert.That(read.Rules.For("bbb").CountsLetters, Is.True, "Hani is written without spaces");
            Assert.That(read.Rules.For("aaa").StopWords, Is.EquivalentTo(new[] { "and", "the" }));
            Assert.That(read.Rules.For("ccc"), Is.SameAs(QualityRules.Spaced), "a known language has no rule data");
        });
    }

    [Test]
    public void APackIsCanonicalWhateverTheOrderItIsGivenInTest()
    {
        var one = SmallPack().ToBytes();
        var two = SmallPack(reversed: true).ToBytes();

        Assert.That(two, Is.EqualTo(one));
    }

    [Test]
    public void AWitlid1ModelReadsAndWritesAsBeforeTest()
    {
        var bytes = File.ReadAllBytes(Fixture(WITLID1_FIXTURE));
        var model = LanguageModel.FromBytes(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(model.Format, Is.EqualTo(LanguageModelFormat.WITLID1));
            Assert.That(model.ToBytes(), Is.EqualTo(bytes));
            Assert.That(model.Sha256(), Is.EqualTo(WITLID1_SHA256));
            Assert.That(model.Acceptance, Is.Null, "the nearest profile always wins");
            Assert.That(model.Rules, Is.SameAs(LanguageRuleSet.Witlid1), "the rule data of Corpus 0.1.14");
            Assert.That(model.Languages, Is.EqualTo(model.Profiles.Select(profile => profile.Code)));
            Assert.That(model.Name, Is.Empty);
            Assert.That(model.Metadata, Is.Empty);
        });
    }

    #endregion

    #region Refusal Tests

    [Test]
    public void APackOutOfItsCanonicalOrderIsRefusedTest()
    {
        // Two stop words written in reverse order: the same meaning, other bytes - refused, so
        // one meaning has one hash.
        var bytes = SmallPack().ToBytes();
        var and = Encoding.UTF8.GetBytes("\u0003and\u0003the");
        var swapped = Encoding.UTF8.GetBytes("\u0003the\u0003and");
        var at = IndexOf(bytes, and);
        Assume.That(at, Is.GreaterThan(0));
        Array.Copy(swapped, 0, bytes, at, swapped.Length);

        Assert.That(() => LanguageModel.FromBytes(bytes), Throws.InstanceOf<FormatException>().With.Message.Contains("canonical"));
    }

    [Test]
    public void APackWithBytesAfterItsEndIsRefusedTest()
    {
        var bytes = SmallPack().ToBytes().Append((byte)0).ToArray();

        Assert.That(() => LanguageModel.FromBytes(bytes), Throws.InstanceOf<FormatException>());
    }

    [Test]
    public void ATruncatedPackIsRefusedTest()
    {
        var bytes = SmallPack().ToBytes();

        Assert.Multiple(() =>
        {
            foreach (var length in new[] { 3, 10, bytes.Length / 2, bytes.Length - 1 })
                Assert.That(() => LanguageModel.FromBytes(bytes[..length]), Throws.InstanceOf<FormatException>(), $"{length} bytes");
        });
    }

    [Test]
    public void AThresholdThisControllerDoesNotHaveIsRefusedTest()
    {
        var bytes = SmallPack().ToBytes();
        var known = Encoding.UTF8.GetBytes("top_ngram_chars");
        var unknown = Encoding.UTF8.GetBytes("top_ngram_charz");
        var at = IndexOf(bytes, known);
        Assume.That(at, Is.GreaterThan(0));
        Array.Copy(unknown, 0, bytes, at, unknown.Length);

        Assert.That(() => LanguageModel.FromBytes(bytes), Throws.InstanceOf<FormatException>().With.Message.Contains("top_ngram_charz"));
    }

    [Test]
    public void AnUnknownFormatIsRefusedTest()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("WITLID9");
            writer.Write(0);
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => LanguageModel.FromBytes(stream.ToArray()), Throws.InstanceOf<FormatException>());
            Assert.That(() => LanguageModel.FromBytes([]), Throws.InstanceOf<FormatException>());
            Assert.That(() => LanguageModel.FromBytes(Encoding.ASCII.GetBytes("not a model at all")), Throws.InstanceOf<FormatException>());
        });
    }

    [Test]
    public void APackWhosePartsDoNotFitIsNotBuiltTest()
    {
        var profiles = new[] { Profile("aaa", "a", "b"), Profile("bbb", "c", "d") };
        var rules = new LanguageRuleSet([Rules("aaa", "Latn")]);

        Assert.Multiple(() =>
        {
            Assert.That(() => new LanguageModel("x", profiles, new LanguageRuleSet([Rules("zzz", "Latn")]), new Dictionary<string, int>(), new Dictionary<string, string>()),
                Throws.ArgumentException, "rule data without a profile");
            Assert.That(() => new LanguageModel("x", profiles, rules, new Dictionary<string, int> { ["bbb"] = 5 }, new Dictionary<string, string>()),
                Throws.ArgumentException, "a limit for a language the pack does not support");
            Assert.That(() => new LanguageModel("x", profiles, new LanguageRuleSet([]), new Dictionary<string, int>(), new Dictionary<string, string>()),
                Throws.ArgumentException, "no supported language");
            Assert.That(() => new LanguageModel("x", [.. profiles, Profile("aaa", "e")], rules, new Dictionary<string, int>(), new Dictionary<string, string>()),
                Throws.ArgumentException, "two profiles of one language");
        });
    }

    #endregion

    #region Tools

    private static LanguageModel SmallPack(bool reversed = false)
    {
        var profiles = new List<LanguageProfile> { Profile("aaa", "a", "_a", "an"), Profile("bbb", "中", "国"), Profile("ccc", "c", "_c") };
        var rules = new List<LanguageRules>
        {
            new("aaa", "Latn", reversed ? ["the", "and", "the"] : ["and", "the"], 2, [new("min_words", [40.0])]),
            new("bbb", "Hani", [], 0, reversed
                ? [new(QualityRuleOverrides.TOP_NGRAM_CHARS, [0.3, 0.2, 0.1]), new("max_duplicate_line_ratio", [0.25])]
                : [new("max_duplicate_line_ratio", [0.25]), new(QualityRuleOverrides.TOP_NGRAM_CHARS, [0.3, 0.2, 0.1])])
        };
        var limits = new Dictionary<string, int> { ["aaa"] = 400_000, ["bbb"] = 0 };
        var metadata = new Dictionary<string, string> { ["recall"] = "0.999", ["built_by"] = "a test" };

        if (reversed)
        {
            profiles.Reverse();
            rules.Reverse();
        }

        return new LanguageModel("tiny", profiles, new LanguageRuleSet(rules), limits, metadata);
    }

    private static LanguageProfile Profile(string code, params string[] grams)
    {
        return new LanguageProfile(code, grams);
    }

    private static LanguageRules Rules(string code, string script)
    {
        return new LanguageRules(code, script, [], 0, []);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
