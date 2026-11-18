using OutWit.Controller.AI.Corpus.Tests.Mock;
using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// Audit finding H-4: a language pack is a lab's input that every node of a job loads. It cost a
/// node 64 times its size in memory, had no limit on its size or its counts, was allocated for by
/// the numbers its header stated, and stayed loaded for the life of the process.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LanguagePackH4Tests
{
    #region Constants

    private const string PACK_FIXTURE = CorpusFixtures.PACK;
    private const string MODEL_FIXTURE = CorpusFixtures.WITLID1;

    #endregion

    #region Setup

    [SetUp]
    [TearDown]
    public void Forget()
    {
        ShardLanguageModels.Clear();
    }

    #endregion

    #region Tests

    [TestCase(2_000_000)]
    [TestCase(8_000_000)]
    public void H4APackOfEmptyGramsIsRefusedTest(int grams)
    {
        // The audit's pack: one profile listing millions of empty n-grams, a byte each. 2 MB of
        // it held 128 MB once loaded, 8 MB held 512 MB.
        var bytes = Pack(writer =>
        {
            writer.Write("eng");
            writer.Write(grams);
            for (var i = 0; i < grams; i++)
                writer.Write("");
            Supported(writer);
        });

        var before = GC.GetAllocatedBytesForCurrentThread();
        var facts = CorpusLanguageModelFacts.Read(bytes);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(() => LanguageModel.FromBytes(bytes), Throws.TypeOf<FormatException>());
            Assert.That(facts.Problem, Does.Contain("more than the 16384 a model may"), "refused by name, at the count");
            Assert.That(allocated, Is.LessThan(1_000_000), "and before anything is allocated for it");
        });
    }

    [TestCase(int.MaxValue)]
    [TestCase(1_000_000_000)]
    [TestCase(100_000)]
    [TestCase(-1)]
    public void H4AHeaderCountIsNeverAllocatedForTest(int profiles)
    {
        // Twelve bytes of WITLID1 that claim a number of profiles: 2,147,483,647 threw
        // OutOfMemoryException out of the server's reading, 1,000,000,000 allocated 8 GB first.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("WITLID1");
            writer.Write(profiles);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        CorpusLanguageModelFacts? facts = null;
        Assert.That(() => facts = CorpusLanguageModelFacts.Read(stream.ToArray()), Throws.Nothing);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(facts!.Problem, Is.Not.Null);
            Assert.That(allocated, Is.LessThan(1_000_000));
        });
    }

    [Test]
    public void H4EveryCountOfAPackHasALimitTest()
    {
        (string Name, byte[] Bytes, string Problem)[] packs =
        [
            ("too many profiles", Pack(513, index => writer => Profile(writer, $"l{index:D4}", ["ab"])), "513 profiles, more than the 512"),
            ("too many n-grams in all", Pack(40, index => writer => Profile(writer, $"l{index:D4}", Enumerable.Range(0, 16_000).Select(Gram))), "more than 524288 n-grams in all"),
            ("an n-gram of five characters", Pack(writer => Profile(writer, "eng", ["abcde"])), "n-gram of more than 4 characters"),
            ("an n-gram twice", Pack(writer => Profile(writer, "eng", ["ab", "cd", "ab"])), "lists an n-gram twice"),
            ("an empty n-gram", Pack(writer => Profile(writer, "eng", ["ab", ""])), "holds an empty n-gram"),
            ("a code that is a path", Pack(writer => Profile(writer, "..\\..\\x", ["ab"])), "language code that is not a short word"),
            ("a code of forty characters", Pack(writer => Profile(writer, new string('a', 40), ["ab"])), "language code of more than 16 characters"),
            ("too many stop words", Pack(writer => { writer.Write("eng"); writer.Write(1); writer.Write("ab"); Supported(writer, stopWords: 5000); }), "5000 eng stop words, more than the 4096"),
            ("too many metadata entries", Pack(writer => Profile(writer, "eng", ["ab"]), metadata: 100), "100 metadata entries, more than the 64")
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, bytes, problem) in packs)
                Assert.That(CorpusLanguageModelFacts.Read(bytes).Problem, Does.Contain(problem), name);
        });
    }

    [Test]
    public void H4AModelFileHasASizeLimitAskedBeforeItIsReadTest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"witai_corpus_pack_{Guid.NewGuid():N}.bin");
        try
        {
            // A file of 17 MiB that claims to be a model: its length is all that is looked at.
            using (var file = new FileStream(path, FileMode.Create))
                file.SetLength(LanguageModelLimits.MAX_BYTES + 1L);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var facts = CorpusLanguageModelFacts.Load(path);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Multiple(() =>
            {
                Assert.That(facts.Problem, Is.EqualTo("the language model is larger than 16777216 bytes"));
                Assert.That(allocated, Is.LessThan(1_000_000), "not read to be refused");
                Assert.That(() => LanguageModel.Load(path), Throws.TypeOf<FormatException>());
                Assert.That(() => ShardLanguageModels.Load(path, ""), Throws.TypeOf<FormatException>());
                Assert.That(() => LanguageModel.FromBytes(new byte[LanguageModelLimits.MAX_BYTES + 1]), Throws.TypeOf<FormatException>());
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void H4ALoadedPackCostsWhatItsLimitsAllowTest()
    {
        // The largest pack the limits let through: 32 profiles of 16,384 n-grams. What it holds
        // once loaded is bounded by its n-grams, not by a number in its header.
        var bytes = Pack(32, index => writer => Profile(writer, $"l{index:D4}", Enumerable.Range(0, 16_384).Select(Gram)));

        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var model = LanguageModel.FromBytes(bytes);
        var retained = GC.GetTotalMemory(true) - before;

        Assert.Multiple(() =>
        {
            Assert.That(model.Profiles.Sum(profile => profile.Grams.Count), Is.EqualTo(LanguageModelLimits.MAX_TOTAL_GRAMS));
            Assert.That(retained, Is.LessThan(96L * 1024 * 1024), "about 64 bytes an n-gram and its string");
        });
        GC.KeepAlive(model);
    }

    [Test]
    public void H4ANodeKeepsTheLastFewModelsNotEveryModelTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_packs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var paths = new List<(string Path, string Sha256)>();
            for (var index = 0; index < LanguageModelLimits.MAX_CACHED + 3; index++)
            {
                var path = Path.Combine(directory, $"pack-{index}.bin");
                File.WriteAllBytes(path, Pack(writer => Profile(writer, "eng", [$"a{index}"])));
                paths.Add((path, ShardLanguageModels.Hash(path)));
            }

            foreach (var (path, sha256) in paths)
                ShardLanguageModels.Load(path, sha256);

            var newest = ShardLanguageModels.Load(paths[^1].Path, paths[^1].Sha256);
            var oldest = ShardLanguageModels.Load(paths[0].Path, paths[0].Sha256);

            Assert.Multiple(() =>
            {
                Assert.That(ShardLanguageModels.Cached, Is.EqualTo(LanguageModelLimits.MAX_CACHED), "twenty jobs with twenty packs do not add up");
                Assert.That(ShardLanguageModels.Load(paths[^1].Path, paths[^1].Sha256), Is.SameAs(newest), "a model in use stays loaded");
                Assert.That(oldest.Profiles[0].Grams[0], Is.EqualTo("a0"), "one that went is read again when a job asks");
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void H4TheReferencePacksAreWellWithinTheLimitsTest()
    {
        var pack = LanguageModel.Load(Path.Combine(Fixtures(), PACK_FIXTURE));
        var model = LanguageModel.Load(Path.Combine(Fixtures(), MODEL_FIXTURE));

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(Path.Combine(Fixtures(), PACK_FIXTURE)).Length * 20, Is.LessThan(LanguageModelLimits.MAX_BYTES), "twenty times the reference pack fits");
            Assert.That(pack.Profiles.Count * 10, Is.LessThan(LanguageModelLimits.MAX_PROFILES));
            Assert.That(pack.Profiles.Max(profile => profile.Grams.Count) * 5, Is.LessThan(LanguageModelLimits.MAX_GRAMS));
            Assert.That(pack.Profiles.Sum(profile => profile.Grams.Count) * 4, Is.LessThan(LanguageModelLimits.MAX_TOTAL_GRAMS));
            Assert.That(pack.ToBytes(), Is.EqualTo(File.ReadAllBytes(Path.Combine(Fixtures(), PACK_FIXTURE))), "read and written again, the same bytes");
            Assert.That(model.ToBytes(), Is.EqualTo(File.ReadAllBytes(Path.Combine(Fixtures(), MODEL_FIXTURE))));
        });
    }

    #endregion

    #region Tools

    private static string Gram(int index)
    {
        // Distinct n-grams of three characters.
        return new string([(char)('a' + index % 26), (char)('a' + index / 26 % 26), (char)(0x400 + index / 676)]);
    }

    /// <summary>A WITLID2 pack of one profile written by the caller.</summary>
    private static byte[] Pack(Action<BinaryWriter> profile, int metadata = 0)
    {
        return Pack(1, _ => profile, metadata);
    }

    /// <summary>A WITLID2 pack of the given number of profiles, in code order.</summary>
    private static byte[] Pack(int profiles, Func<int, Action<BinaryWriter>> profile, int metadata = 0)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("WITLID2");
            writer.Write("hostile");
            writer.Write(metadata);
            for (var index = 0; index < metadata; index++)
            {
                writer.Write($"key{index:D4}");
                writer.Write("value");
            }

            writer.Write(profiles);
            for (var index = 0; index < profiles; index++)
                profile(index)(writer);
        }

        return stream.ToArray();
    }

    /// <summary>A supported profile with the given n-grams and no rule data of note.</summary>
    private static void Profile(BinaryWriter writer, string code, IEnumerable<string> grams)
    {
        var list = grams.ToList();
        writer.Write(code);
        writer.Write(list.Count);
        foreach (var gram in list)
            writer.Write(gram);
        Supported(writer);
    }

    private static void Supported(BinaryWriter writer, int stopWords = 0)
    {
        writer.Write(true);
        writer.Write("Latn");
        writer.Write(0);
        writer.Write(stopWords);
        for (var index = 0; index < stopWords; index++)
            writer.Write($"w{index:D6}");
        writer.Write(0);
        writer.Write(0);
    }

    private static string Fixtures()
    {
        return CorpusFixtures.Folder;
    }

    #endregion
}
