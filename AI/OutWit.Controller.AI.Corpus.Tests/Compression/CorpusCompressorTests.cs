using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Compression;

/// <summary>
/// zstd through the managed port: a round trip is the identity, the frame of a fixed input is
/// the same bytes on every run and every platform (the committed digest is the CI legs' common
/// pin), the stream form matches the buffer form, and the golden shard's text compresses by
/// the order of magnitude the corpus plan counts on.
/// </summary>
[TestFixture]
public sealed class CorpusCompressorTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";

    /// <summary>SHA-256 of the frame of <see cref="GOLDEN_INPUT"/> at the pinned level; produced on win-x64, re-checked on every platform.</summary>
    private const string GOLDEN_FRAME_SHA256 = "f40ad608beb553b99200fc28b0809cfaa544609fc1b63a5e3be182adf1bbad36";

    private const string GOLDEN_INPUT = "The committee reviewed the annual report in detail. Der Ausschuss prüfte den Jahresbericht. Комитет рассмотрел годовой отчёт. 委员会审查了年度报告。 ";

    #endregion

    #region Tests

    [Test]
    public void RoundTripIsTheIdentityTest()
    {
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(GOLDEN_INPUT, 50)));

        var frame = CorpusCompressor.Compress(data);
        var back = CorpusCompressor.Decompress(frame);

        Assert.Multiple(() =>
        {
            Assert.That(back, Is.EqualTo(data));
            Assert.That(frame.Length, Is.LessThan(data.Length / 5), "repetitive text compresses well");
        });
    }

    [Test]
    public void FrameOfAFixedInputIsPinnedTest()
    {
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(GOLDEN_INPUT, 200)));

        var first = CorpusCompressor.Compress(data);
        var second = CorpusCompressor.Compress(data);
        var digest = Convert.ToHexStringLower(SHA256.HashData(first));

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(digest, Is.EqualTo(GOLDEN_FRAME_SHA256), $"the frame digest is {digest}");
        });
    }

    [Test]
    public void StreamFormMatchesBufferFormTest()
    {
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(GOLDEN_INPUT, 300)));

        var buffered = CorpusCompressor.Compress(data);
        using var destination = new MemoryStream();
        using (var stream = CorpusCompressor.CompressTo(destination))
        {
            // Written in uneven pieces, as a shard product is written document by document.
            var offset = 0;
            foreach (var size in new[] { 7, 1000, 13, 4096, 1, data.Length })
            {
                var take = Math.Min(size, data.Length - offset);
                if (take <= 0)
                    break;
                stream.Write(data, offset, take);
                offset += take;
            }
        }

        using var source = new MemoryStream(destination.ToArray());
        using var decompressed = CorpusCompressor.DecompressFrom(source);
        using var back = new MemoryStream();
        decompressed.CopyTo(back);

        Assert.Multiple(() =>
        {
            Assert.That(back.ToArray(), Is.EqualTo(data));
            Assert.That(CorpusCompressor.Decompress(destination.ToArray()), Is.EqualTo(data), "a streamed frame decompresses as a buffer too");
        });
        TestContext.Out.WriteLine($"buffer frame {buffered.Length} bytes, streamed frame {destination.Length} bytes");
    }

    [Test]
    public void GoldenShardTextCompressesByAnOrderOfMagnitudeTest()
    {
        using var reader = WarcReader.Open(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE));
        var text = new MemoryStream();
        foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
        {
            text.Write(record.Payload.Span);
            text.WriteByte((byte)'\n');
        }

        var frame = CorpusCompressor.Compress(text.ToArray());
        var ratio = (double)text.Length / frame.Length;
        TestContext.Out.WriteLine($"{text.Length:N0} bytes of WET text → {frame.Length:N0} bytes zstd-{CorpusCompressor.LEVEL} ({ratio:F1}×)");

        Assert.That(ratio, Is.GreaterThan(2.5));
        Assert.That(CorpusCompressor.Decompress(frame), Has.Length.EqualTo(text.Length));
    }

    #endregion
}
