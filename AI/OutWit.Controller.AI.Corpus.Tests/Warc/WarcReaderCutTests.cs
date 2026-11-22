using System.IO.Compression;
using System.Text;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Warc;

/// <summary>
/// A shard that stops anywhere: the reader gives the records that are whole before the cut and
/// says the shard is truncated, and never calls the cut a malformed record. The last line of a
/// file that has no line feed is the line the cut went through; it used to be read as a whole
/// line, so a version line that lost its end was "not a WARC version line", a header that lost
/// its colon was "a malformed header line" and a length that lost its digits was "a bad
/// Content-Length": one cut in eighty of a real WET file made a shard malformed that was a
/// partial download.
/// <para>
/// What a cut has to give is worked out by <see cref="WarcCutModel"/> from the framing of the
/// whole stream, not by the reader: every byte of the headers of every record of the two
/// generated shards, every byte of their first records, five thousand cuts over each shard
/// unpacked and a thousand over each file as it is packed, the heads of a real WET file and of
/// a real WARC file among them (the cases of the category <c>Crawl</c>).
/// </para>
/// </summary>
[TestFixture]
public sealed class WarcReaderCutTests
{
    #region Constants

    private const string WET = "wet";
    private const string WARC = "warc";

    /// <summary>The cuts of a sweep over a whole shard.</summary>
    private const int CUTS = 5000;

    /// <summary>How many failures a test names; it counts them all.</summary>
    private const int SHOWN = 8;

    private const string FORMAT_ERROR = "a format error";

    #endregion

    #region Fields

    private IReadOnlyList<byte[]> m_wet = null!;
    private IReadOnlyList<byte[]> m_warc = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_wet = CrawlSynthShards.WetRecords(CorpusFixtures.Folder);
        m_warc = CrawlSynthShards.WarcRecords(CorpusFixtures.Folder);
    }

    #endregion

    #region Sweep Tests

    [TestCase(WET)]
    [TestCase(WARC)]
    public void ACutAtAnyByteOfARecordsHeadersIsTheTruncatedTailTest(string format)
    {
        // Every record of the shard in turn, after one whole record (the first stands alone),
        // cut at each byte from its first to the last of its headers.
        var records = Records(format);
        var wrong = new List<string>();
        var cuts = 0;

        for (var index = 0; index < records.Count; index++)
        {
            byte[] stream = index == 0 ? records[0] : [.. records[0], .. records[index]];
            var model = WarcCutModel.Of(stream);
            var first = index == 0 ? 0 : records[0].Length;

            for (var position = first; position <= model.HeaderEnd(model.Count - 1); position++)
            {
                cuts++;
                Check(stream, position, model, position, $"record {index}", wrong);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(wrong.Count, Is.Zero, Summary(wrong, cuts));
            Assert.That(cuts, Is.GreaterThan(records.Count * 200), "every byte of every record's headers");
        });
    }

    [TestCase(WET, 6)]
    [TestCase(WARC, 7)]
    public void ACutAtAnyByteOfTheFirstRecordsGivesTheRecordsBeforeItTest(string format, int count)
    {
        // The headers, the payloads and the blank lines between: every byte of a few records.
        byte[] stream = [.. Records(format).Take(count).SelectMany(record => record)];
        var model = WarcCutModel.Of(stream);
        var wrong = new List<string>();

        for (var position = 0; position <= stream.Length; position++)
            Check(stream, position, model, position, "the first records", wrong);

        Assert.Multiple(() =>
        {
            Assert.That(model.Count, Is.EqualTo(count));
            Assert.That(wrong.Count, Is.Zero, Summary(wrong, stream.Length + 1));
        });
    }

    [TestCase(CorpusFixtures.WET, 430)]
    [TestCase(CorpusFixtures.WARC, 367)]
    [TestCase(CorpusFixtures.CRAWL_WET, 437, Category = CorpusFixtures.CRAWL)]
    [TestCase(CorpusFixtures.CRAWL_WARC, 350, Category = CorpusFixtures.CRAWL)]
    public void ADenseSweepOfCutsOverAShardGivesTheRecordsBeforeEachTest(string fixture, int count)
    {
        // Five thousand cuts spread over the shard's unpacked stream. 63 of them were a format
        // error on the head of the real WET file, 331 on the generated WET shard.
        var packed = File.ReadAllBytes(CorpusFixtures.PathOf(fixture));
        var stream = Unpack(packed, packed.Length);
        var model = WarcCutModel.Of(stream);
        var wrong = new List<string>();

        for (var index = 1; index <= CUTS; index++)
        {
            var position = Position(stream.Length, index, CUTS);
            Check(stream, position, model, position, fixture, wrong);
        }

        Assert.Multiple(() =>
        {
            Assert.That(model.Count, Is.EqualTo(count));
            Assert.That(wrong.Count, Is.Zero, Summary(wrong, CUTS));
        });
    }

    [TestCase(CorpusFixtures.WET)]
    [TestCase(CorpusFixtures.WARC)]
    [TestCase(CorpusFixtures.CRAWL_WET, Category = CorpusFixtures.CRAWL)]
    [TestCase(CorpusFixtures.CRAWL_WARC, Category = CorpusFixtures.CRAWL)]
    public void ACutOfAPackedShardGivesTheRecordsItsBytesHoldTest(string fixture)
    {
        // The file as it travels: gzip, a member a record, cut where a connection may drop it.
        // What is left of it unpacks to a head of the whole stream; the model says what that
        // head holds.
        const int cuts = 1000;
        var packed = File.ReadAllBytes(CorpusFixtures.PathOf(fixture));
        var model = WarcCutModel.Of(Unpack(packed, packed.Length));
        var held = Held(packed);
        var wrong = new List<string>();
        var unseen = 0;

        Assert.That(held[^1], Is.EqualTo(model.Length), "the whole file unpacks to the whole stream");

        for (var index = 1; index <= cuts; index++)
        {
            var length = Position(packed.Length, index, cuts);
            if (index % 100 == 0)
                Assert.That(Unpack(packed, length).Length, Is.EqualTo(held[length]), $"what {length} bytes of the file unpack to");

            if (length < packed.Length && !model.At(held[length]).Truncated)
                unseen++;

            Check(packed, length, model, held[length], fixture, wrong);
        }

        // A cut in the gzip frame between two members, or in the tables the next member's data
        // starts with, gives no byte of the next record: what unpacks is a shorter whole file.
        TestContext.Out.WriteLine($"{fixture}: {cuts} cuts of {packed.Length} bytes, {model.Count} records; {unseen} cuts leave what unpacks at the end of a record.");

        Assert.That(wrong.Count, Is.Zero, Summary(wrong, cuts));
    }

    #endregion

    #region Line Tests

    [TestCase("W")]
    [TestCase("WARC")]
    [TestCase("WARC/1.0")]
    [TestCase("WARC/1.0\r")]
    [TestCase("\r")]
    [TestCase("\r\n\r")]
    [TestCase("WARC/1.0\r\nWARC-Ty")]
    [TestCase("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length:")]
    [TestCase("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length: -")]
    [TestCase("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length: 12\r\n\r")]
    [TestCase("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length: 0\r\n\r")]
    public void AFileThatStopsInsideALineEndsWithItsWholeRecordsTest(string tail)
    {
        using var reader = new WarcReader(new MemoryStream([.. Record("http://a/", "hello"), .. Encoding.ASCII.GetBytes(tail)]));

        var records = reader.ReadRecords().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records.Select(record => record.TargetUri), Is.EqualTo(new[] { "http://a/" }), "the record before the cut, and no half of the next");
            Assert.That(reader.Truncated, Is.True);
        });
    }

    [TestCase("this is not a warc record")]
    [TestCase("HTTP/1.1 503 Service Unavailable")]
    [TestCase("WARX")]
    [TestCase("this is not a warc record\r\n")]
    [TestCase("WARC/1.0\r\nno colon in this line\r\n")]
    [TestCase("WARC/1.0\r\nContent-Length: twelve\r\n")]
    [TestCase("WARC/1.0\r\nWARC-Type: conversion\r\n\r\n")]
    public void ALineThatIsNoPartOfARecordIsStillAnErrorTest(string tail)
    {
        // A line with its line feed is judged as before; so is a last line where a record
        // should start that no cut of a version line leaves: a body that is no WARC is not a
        // truncated WARC.
        using var reader = new WarcReader(new MemoryStream([.. Record("http://a/", "hello"), .. Encoding.ASCII.GetBytes(tail)]));

        Assert.That(() => reader.ReadRecords().ToList(), Throws.InstanceOf<WarcFormatException>().With.Message.Contains("offset"));
    }

    [Test]
    public void AFileThatEndsAfterARecordIsWholeTest()
    {
        // The end of a payload, one blank line, two: nothing was begun, nothing is truncated.
        var record = Record("http://a/", "hello");

        foreach (var keep in new[] { record.Length - 4, record.Length - 2, record.Length })
        {
            using var reader = new WarcReader(new MemoryStream(record, 0, keep, writable: false));

            Assert.Multiple(() =>
            {
                Assert.That(reader.ReadRecords().Count(), Is.EqualTo(1), $"{keep} bytes of {record.Length}");
                Assert.That(reader.Truncated, Is.False, $"{keep} bytes of {record.Length}");
            });
        }
    }

    #endregion

    #region Tools

    private IReadOnlyList<byte[]> Records(string format)
    {
        return format == WET ? m_wet : m_warc;
    }

    /// <summary>Reads the first bytes of an input and holds the outcome to the model at the position those bytes unpack to.</summary>
    private static void Check(byte[] input, int length, WarcCutModel model, int held, string what, List<string> wrong)
    {
        var (whole, truncated) = model.At(held);
        string? problem;
        try
        {
            using var reader = new WarcReader(new MemoryStream(input, 0, length, writable: false));
            var records = reader.ReadRecords().ToList();

            problem = model.Mismatch(records, whole);
            if (problem == null && reader.Truncated != truncated)
                problem = $"Truncated is {reader.Truncated} with {whole} whole records before the cut";
        }
        catch (WarcFormatException exception)
        {
            problem = $"{FORMAT_ERROR}: {exception.Message}";
        }

        if (problem != null)
            wrong.Add($"  {what}, {length} bytes: {problem}");
    }

    /// <summary>How many cuts went wrong, how many of them as a format error, and the first of them.</summary>
    private static string Summary(List<string> wrong, int cuts)
    {
        var errors = wrong.Count(line => line.Contains(FORMAT_ERROR, StringComparison.Ordinal));
        return $"{wrong.Count} of {cuts} cuts, {errors} of them a format error; the first:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(SHOWN))}";
    }

    /// <summary>The cuts of a sweep: spread evenly, and moved by a few bytes so that they do not all fall on the same phase of anything.</summary>
    private static int Position(int total, int index, int cuts)
    {
        return (int)Math.Min(total, (long)total * index / (cuts + 1) + index % 7);
    }

    /// <summary>
    /// How many bytes a decompressor gives of every head of a packed file, in one pass: the
    /// file is fed to it a byte at a time, and what has come out by the time a byte is asked
    /// for is what the bytes before it hold.
    /// </summary>
    private static int[] Held(byte[] packed)
    {
        var held = new int[packed.Length + 1];
        var source = new TrickleStream(packed, 1);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        var buffer = new byte[1 << 16];
        var total = 0;
        var known = 0;

        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            for (var fed = (int)source.Position; known < fed; known++)
                held[known] = total;

            if (read == 0)
                break;

            total += read;
        }

        for (; known <= packed.Length; known++)
            held[known] = total;

        return held;
    }

    /// <summary>What a decompressor gives of the first bytes of a packed file.</summary>
    private static byte[] Unpack(byte[] packed, int length)
    {
        using var gzip = new GZipStream(new MemoryStream(packed, 0, length, writable: false), CompressionMode.Decompress);
        using var unpacked = new MemoryStream();
        gzip.CopyTo(unpacked);
        return unpacked.ToArray();
    }

    private static byte[] Record(string uri, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = $"WARC/1.0\r\nWARC-Type: conversion\r\nWARC-Target-URI: {uri}\r\nContent-Type: text/plain\r\nContent-Length: {bytes.Length}\r\n\r\n";
        return [.. Encoding.ASCII.GetBytes(header), .. bytes, .. "\r\n\r\n"u8];
    }

    #endregion
}
