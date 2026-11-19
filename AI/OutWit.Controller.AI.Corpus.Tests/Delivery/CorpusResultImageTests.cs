using System.Buffers.Binary;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Tests.Model;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Delivery;

/// <summary>
/// The walk of a result's image: a node chose every byte of a result frame, and the decoder is
/// handed an image only after every head in it (the member count of an object, the count of a
/// list, the lengths of a string) was found inside the image and inside the bound of its kind.
/// The layout the walk follows is held to the serializer's own bytes, every head is bent in turn
/// into what a dishonest node would write there, and a frame whose string states a length
/// outside the frame is given as no result without the decoder being asked.
/// </summary>
[TestFixture]
public sealed class CorpusResultImageTests
{
    #region Constants

    /// <summary>String heads that state a UTF-8 length of two gigabytes and more: the complement of each is at the end of the 32-bit range.</summary>
    private static readonly int[] LENGTHS_PAST_ANY_FRAME = [int.MinValue, int.MinValue + 1, int.MinValue + 2, int.MinValue + 3];

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_image_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Layout

    [Test]
    public void TheWalkFollowsTheLayoutTheSerializerWritesTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, result) in Shapes())
            {
                var written = MemoryPackSerializer.Serialize(result);
                var byHand = CorpusTestImage.Of(result).ToArray();

                Assert.That(byHand, Is.EqualTo(written), $"{name}: the layout the tests state is the serializer's");
                Assert.That(CorpusResultImage.IsSound(written), Is.True, $"{name}: an image an honest node writes is walked to its last byte");
            }
        });
    }

    [Test]
    public void TheWalkReadsBothFormsOfAStringTest()
    {
        // The wire has two forms of a string; the serializer writes UTF-8 and reads both.
        Assert.Multiple(() =>
        {
            foreach (var (name, result) in Shapes())
            {
                var written = MemoryPackSerializer.Serialize(result, MemoryPackSerializerOptions.Utf16);
                var byHand = CorpusTestImage.Of(result, utf16: true).ToArray();

                Assert.That(byHand, Is.EqualTo(written), name);
                Assert.That(CorpusResultImage.IsSound(written), Is.True, name);
                Assert.That(MemoryPackSerializer.Serialize(MemoryPackSerializer.Deserialize<CorpusResultData>(written)), Is.EqualTo(MemoryPackSerializer.Serialize(result)), $"{name}: read back, it is the result that was written");
            }
        });
    }

    [Test]
    public void TheMemberCountsOfTheWalkAreTheModelsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultImage.RESULT_MEMBERS, Is.EqualTo(CorpusModelWireLayout.Members(typeof(CorpusResultData)).Count));
            Assert.That(CorpusResultImage.STATS_MEMBERS, Is.EqualTo(CorpusModelWireLayout.Members(typeof(CorpusShardStatsData)).Count));
            Assert.That(CorpusResultImage.COUNT_MEMBERS, Is.EqualTo(CorpusModelWireLayout.Members(typeof(CorpusCountData)).Count));
            Assert.That(CorpusResultImage.PDF_MEMBERS, Is.EqualTo(CorpusModelWireLayout.Members(typeof(CorpusPdfStatsData)).Count));
            Assert.That(CorpusResultImage.MINHASH_MEMBERS, Is.EqualTo(CorpusModelWireLayout.Members(typeof(CorpusMinHashData)).Count));
        });
    }

    [Test]
    public void ANullResultIsNoImageOfAResultTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize<CorpusResultData>(null)), Is.False);
            Assert.That(CorpusResultImage.IsSound([]), Is.False);
        });
    }

    #endregion

    #region Heads

    [Test]
    public void AnObjectWithOtherMembersThanItsTypeHasIsRefusedTest()
    {
        var image = Rich();

        Assert.Multiple(() =>
        {
            foreach (var head in image.Heads.Where(head => head.Kind == CorpusTestImageHeadKind.Object))
            {
                var members = image.ToArray()[head.Offset];
                for (var count = 0; count < CorpusTestImage.NULL_OBJECT; count++)
                {
                    if (count != members)
                        Assert.That(CorpusResultImage.IsSound(image.With(head, count)), Is.False, $"{head}: {count} members where the type has {members}");
                }
            }
        });
    }

    [Test]
    public void AListCountOutsideItsBoundIsRefusedTest()
    {
        var image = Rich();
        int[] counts = [int.MinValue, -1000, -2, CorpusResultCheck.MAX_UNSUPPORTED + 1, 100_000_000, int.MaxValue];

        Assert.Multiple(() =>
        {
            var lists = image.Heads.Where(head => head.Kind == CorpusTestImageHeadKind.List).ToList();
            Assert.That(lists.Select(head => head.Name), Is.EqualTo(new[] { "drops", "languages", "unsupported_languages" }));

            foreach (var head in lists)
            {
                foreach (var count in counts)
                    Assert.That(CorpusResultImage.IsSound(image.With(head, count)), Is.False, $"{head}: a count of {count}");
            }
        });
    }

    [Test]
    public void AStringLengthOutsideTheImageIsRefusedTest()
    {
        var image = Rich();

        Assert.Multiple(() =>
        {
            var strings = image.Heads.Where(head => head.Kind == CorpusTestImageHeadKind.Text).ToList();
            Assert.That(strings, Has.Count.GreaterThanOrEqualTo(12), "the strings of the result and of every count");

            foreach (var head in strings)
            {
                // Two gigabytes of UTF-8, and of UTF-16; one character past the bound, in both forms.
                foreach (var length in LENGTHS_PAST_ANY_FRAME.Concat([int.MaxValue, CorpusResultImage.MAX_TEXT_CHARS + 1, ~(3 * CorpusResultImage.MAX_TEXT_CHARS + 1)]))
                    Assert.That(CorpusResultImage.IsSound(image.With(head, length)), Is.False, $"{head}: a head of {length}");
            }
        });
    }

    [Test]
    public void ACharacterCountAtOddsWithItsBytesIsRefusedTest()
    {
        var image = Rich();
        var bytes = image.ToArray();

        Assert.Multiple(() =>
        {
            var counts = image.Heads.Where(head => head.Kind == CorpusTestImageHeadKind.Chars).ToList();
            Assert.That(counts, Is.Not.Empty);

            foreach (var head in counts)
            {
                // The byte count stands four bytes before the character count, as its complement.
                var utf8 = ~BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(head.Offset - sizeof(int)));

                // No character, fewer bytes than characters, a count nothing can check, a count below zero.
                foreach (var chars in new[] { 0, -1, int.MinValue, utf8 + 1, int.MaxValue })
                    Assert.That(CorpusResultImage.IsSound(image.With(head, chars)), Is.False, $"{head}: {chars} characters in {utf8} bytes");
            }
        });
    }

    [Test]
    public void AnImageCutShortOrWithMoreAfterItIsRefusedTest()
    {
        var bytes = Rich().ToArray();

        Assert.Multiple(() =>
        {
            for (var length = 0; length < bytes.Length; length++)
                Assert.That(CorpusResultImage.IsSound(bytes.AsSpan(0, length)), Is.False, $"the first {length} of {bytes.Length} bytes");

            Assert.That(CorpusResultImage.IsSound([.. bytes, 0]), Is.False, "a byte after the last member");
            Assert.That(CorpusResultImage.IsSound(bytes), Is.True);
        });
    }

    #endregion

    #region Bounds

    [Test]
    public void AListOfMoreThanItsKindHoldsIsRefusedWholeTest()
    {
        // Every entry is really there: the count is refused for what it is, not for missing bytes.
        var task = CorpusTestRun.Task(0, CorpusTestRun.Labelled());

        Assert.Multiple(() =>
        {
            foreach (var (name, max, set) in new (string, int, Action<CorpusShardStatsData, List<CorpusCountData>>)[]
                     {
                         ("drops", CorpusResultCheck.MaxDrops, (stats, counts) => stats.Drops = counts),
                         ("languages", CorpusResultCheck.MAX_LANGUAGES, (stats, counts) => stats.Languages = counts),
                         ("unsupported languages", CorpusResultCheck.MAX_UNSUPPORTED, (stats, counts) => stats.UnsupportedLanguages = counts)
                     })
            {
                var full = CorpusTestRun.Completed(task);
                set(full.Stats!, Names(max));
                var over = CorpusTestRun.Completed(task);
                set(over.Stats!, Names(max + 1));

                Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize(full)), Is.True, $"{name}: {max} entries are the most its kind holds");
                Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize(over)), Is.False, $"{name}: {max + 1} entries");
            }
        });
    }

    [Test]
    public void AStringLongerThanAUrlMayBeIsRefusedTest()
    {
        var task = CorpusTestRun.Task(0);
        var longest = CorpusTestRun.Completed(task);
        longest.SourceUrl = new string('u', CorpusResultImage.MAX_TEXT_CHARS);
        var longer = CorpusTestRun.Completed(task);
        longer.SourceUrl = new string('u', CorpusResultImage.MAX_TEXT_CHARS + 1);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultImage.MAX_TEXT_CHARS, Is.EqualTo(CorpusResultCheck.MAX_URL_CHARS));
            Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize(longest)), Is.True);
            Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize(longer)), Is.False);
            Assert.That(CorpusResultImage.IsSound(MemoryPackSerializer.Serialize(longer, MemoryPackSerializerOptions.Utf16)), Is.False);
        });
    }

    #endregion

    #region The reading of a package

    [Test]
    public void AFrameWhoseStringPointsOutsideItNeverReachesTheDecoderTest()
    {
        // The released decoder reads a string of a stated UTF-8 length of two gigabytes from
        // where it stands, past the end of its buffer: the process ends, and no catch holds
        // that. Each such frame is given as no result here, and the frame after it is read.
        var tasks = CorpusTestRun.Tasks(2, CorpusTestRun.Signed());
        var image = CorpusTestImage.Of(CorpusTestRun.Completed(tasks[0]));
        var honest = MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[1]));
        var first = image.Heads.First(head => head.Kind == CorpusTestImageHeadKind.Text);

        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("content_sha256"), "the first string of a result");

            foreach (var head in image.Heads.Where(head => head.Kind == CorpusTestImageHeadKind.Text))
            {
                foreach (var length in LENGTHS_PAST_ANY_FRAME)
                {
                    var bent = image.With(head, length);
                    var path = Frames(bent, honest);

                    var back = CorpusPackageCodec.ReadResults(path, 2).ToList();

                    Assert.That(back, Has.Count.EqualTo(2), $"{head}: {length}");
                    Assert.That(back[0], Is.Null, $"{head}: a head of {length} is no result");
                    Assert.That(back[1], Is.Not.Null, $"{head}: the reading goes on");
                }
            }
        });
    }

    [Test]
    public void EveryFrameAnHonestNodeWritesIsReadAsBeforeTest()
    {
        var results = Shapes().Select(shape => shape.Result).ToList();
        var path = Frames(results.Select(result => MemoryPackSerializer.Serialize(result)).ToArray());

        var back = CorpusPackageCodec.ReadResults(path, results.Count).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(back, Has.Count.EqualTo(results.Count));
            for (var index = 0; index < results.Count; index++)
                Assert.That(MemoryPackSerializer.Serialize(back[index]), Is.EqualTo(MemoryPackSerializer.Serialize(results[index])), $"result {index} is the result that was written");
        });
    }

    #endregion

    #region Tools

    /// <summary>Results of every shape a node sends, honest ones and ones the wire can carry (nulls where the types say there are none).</summary>
    private static List<(string Name, CorpusResultData Result)> Shapes()
    {
        var labelled = CorpusTestRun.Task(0, CorpusTestRun.Labelled());
        var signed = CorpusTestRun.Task(1, CorpusTestRun.Signed());
        var plain = CorpusTestRun.Task(2);

        var unsupported = CorpusTestRun.Completed(labelled, documentsOut: 7, languages: [("deu", 3), ("eng", 4)], drops: [("language_unsupported", 3), ("min_words", 2), ("http_status", 5)]);
        unsupported.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("no_profile", 1), ("past_limit:spa", 1), ("swe", 1)]);

        var withPdf = CorpusTestRun.Completed(signed, drops: [("pdf_encrypted", 1)]);
        withPdf.Stats!.Pdf = new CorpusPdfStatsData { In = 4, Truncated = 1, Extracted = 3, Pages = 30, Documents = 5, PagesCut = 1, RunningLines = 60, Fuel = 1_000_000 };
        withPdf.Error = CorpusResultCodes.NOTE_TRUNCATED;

        // Latin, Cyrillic, a character of three bytes and one of four (two units of UTF-16).
        var wideUrl = CorpusTestRun.Completed(plain);
        wideUrl.SourceUrl = "https://data.example/crawl/" + (char)0x00FC + (char)0x0434 + (char)0x6F22 + char.ConvertFromUtf32(0x1F600) + "/shard.warc.wet.gz";

        var nulls = CorpusTestRun.Completed(plain);
        nulls.ContentSha256 = null!;
        nulls.Error = null!;
        nulls.NodeName = null!;
        nulls.Stats!.Drops = null!;
        nulls.Stats.Languages = [null!];

        return
        [
            ("completed", CorpusTestRun.Completed(plain)),
            ("completed with every list", unsupported),
            ("completed, signed, with PDFs", withPdf),
            ("a node fault", CorpusTestRun.Fault(labelled)),
            ("a verdict on the shard", CorpusTestRun.Verdict(plain, CorpusShardVerdict.FetchFailed, "http_404")),
            ("a URL outside ASCII", wideUrl),
            ("nulls where the types say there are none", nulls),
            ("a new result", new CorpusResultData())
        ];
    }

    /// <summary>The image of a result that has every list filled, a PDF block and MinHash parameters, with the places of its heads.</summary>
    private static CorpusTestImage Rich()
    {
        var task = CorpusTestRun.Task(0, new CorpusPipelineData { Version = "test", IdentifyLanguage = true, LanguageModelBlobId = Guid.NewGuid(), LanguageModelSha256 = CorpusTestRun.Hash('1'), Sign = true });
        var result = CorpusTestRun.Completed(task, documentsOut: 7, languages: [("deu", 3), ("eng", 4)], drops: [("language_unsupported", 2), ("min_words", 2)]);
        result.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("no_profile", 1), ("swe", 1)]);
        result.Stats.Pdf = new CorpusPdfStatsData { In = 2, Extracted = 1, Pages = 3, Documents = 1, Fuel = 5000 };
        result.Error = CorpusResultCodes.NOTE_TRUNCATED;

        var image = CorpusTestImage.Of(result);
        Assert.That(image.ToArray(), Is.EqualTo(MemoryPackSerializer.Serialize(result)), "the image is the serializer's");

        return image;
    }

    private static List<CorpusCountData> Names(int count)
    {
        return Enumerable.Range(0, count).Select(index => new CorpusCountData { Name = $"n{index:D4}", Count = 1 }).ToList();
    }

    /// <summary>A result package of the given frames, each an image as it stands.</summary>
    private string Frames(params byte[][] images)
    {
        var path = Path.Combine(m_directory, $"{Guid.NewGuid():N}.wcp");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);

        file.Write("WCPK"u8);
        file.Write([CorpusPackageCodec.KIND_RESULTS, CorpusPackageCodec.VERSION, 0, 0]);

        using var brotli = new BrotliStream(file, CompressionLevel.Fastest, leaveOpen: true);
        Span<byte> head = stackalloc byte[CorpusPackageCodec.FRAME_HEAD_LENGTH];
        foreach (var image in images)
        {
            head[0] = CorpusPackageCodec.FRAME_RESULT;
            BinaryPrimitives.WriteInt32LittleEndian(head[1..], image.Length);
            brotli.Write(head);
            brotli.Write(image);
        }

        return path;
    }

    #endregion
}
