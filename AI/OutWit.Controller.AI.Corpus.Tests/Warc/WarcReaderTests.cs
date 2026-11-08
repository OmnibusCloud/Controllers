using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Warc;

/// <summary>
/// The streamed WARC/WET reader on the golden shards and on hostile input: every record of the
/// Common Crawl fixtures comes out with its headers and exactly its payload, a partial download
/// ends cleanly with the complete records, oversized records are skipped and counted, garbage is
/// an error at an offset, and two reads give the same bytes.
/// </summary>
[TestFixture]
public sealed class WarcReaderTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";

    #endregion

    #region Golden Shard Tests

    [Test]
    public void WetFixtureYieldsEveryConversionRecordTest()
    {
        using var reader = WarcReader.Open(Fixture(WET_FIXTURE));
        var records = reader.ReadRecords().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(437), "one record per gzip member");
            Assert.That(records[0].Type, Is.EqualTo("warcinfo"));
            Assert.That(records.Skip(1).All(r => r.Type == "conversion"), Is.True);
            Assert.That(records.Skip(1).All(r => r.TargetUri != null && r.ContentType == "text/plain"), Is.True);
            Assert.That(records.Skip(1).Count(r => r.IdentifiedLanguage != null), Is.GreaterThan(400), "Common Crawl labels most documents");
            Assert.That(reader.Truncated, Is.False);
            Assert.That(reader.SkippedOversized, Is.Zero);
        });

        var first = records[1];
        var text = Encoding.UTF8.GetString(first.Payload.Span);
        Assert.That(first.TargetUri, Does.StartWith("http"));
        Assert.That(text, Is.Not.Empty);
        Assert.That(first.Payload.Length.ToString(), Is.EqualTo(first.Header("Content-Length")));
    }

    [Test]
    public void WarcFixtureYieldsRequestResponseAndMetadataTripletsTest()
    {
        using var reader = WarcReader.Open(Fixture(WARC_FIXTURE));
        var records = reader.ReadRecords().ToList();
        var types = records.GroupBy(r => r.Type).ToDictionary(g => g.Key, g => g.Count());

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(350));
            Assert.That(types["warcinfo"], Is.EqualTo(1));
            Assert.That(types["request"] - types["response"], Is.InRange(0, 1), "a response per request; the fixture was cut at a member boundary, so the last request may stand alone");
            Assert.That(types["metadata"], Is.EqualTo(types["response"]));
            Assert.That(records.Where(r => r.Type == "response").All(r => r.ContentType!.StartsWith("application/http", StringComparison.Ordinal)), Is.True);
            Assert.That(reader.Truncated, Is.False);
        });

        var response = records.First(r => r.Type == "response");
        var http = Encoding.ASCII.GetString(response.Payload.Span[..Math.Min(200, response.Payload.Length)]);
        Assert.That(http, Does.StartWith("HTTP/1."), "the payload is the HTTP response, headers first");
    }

    [Test]
    public void TwoReadsOfAShardGiveTheSameBytesTest()
    {
        static string Digest(string path)
        {
            using var reader = WarcReader.Open(path);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var record in reader.ReadRecords())
            {
                sha.AppendData(Encoding.UTF8.GetBytes(record.TargetUri ?? ""));
                sha.AppendData(record.Payload.Span);
            }

            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }

        Assert.That(Digest(Fixture(WET_FIXTURE)), Is.EqualTo(Digest(Fixture(WET_FIXTURE))));
    }

    #endregion

    #region Hostile Input Tests

    [Test]
    public void TruncatedDownloadEndsWithTheCompleteRecordsTest()
    {
        var bytes = File.ReadAllBytes(Fixture(WET_FIXTURE));
        using var reader = new WarcReader(new MemoryStream(bytes, 0, bytes.Length * 2 / 3, writable: false));
        var records = reader.ReadRecords().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.InRange(200, 436));
            Assert.That(records.All(r => r.Payload.Length.ToString() == r.Header("Content-Length")), Is.True, "no half record is returned");
        });
    }

    [Test]
    public void OversizedRecordsAreSkippedAndCountedTest()
    {
        var stream = new MemoryStream();
        Append(stream, "conversion", "http://a/", new string('a', 10));
        Append(stream, "conversion", "http://b/", new string('b', 5000));
        Append(stream, "conversion", "http://c/", new string('c', 20));
        stream.Position = 0;

        using var reader = new WarcReader(stream, maxRecordBytes: 1000);
        var records = reader.ReadRecords().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records.Select(r => r.TargetUri), Is.EqualTo(new[] { "http://a/", "http://c/" }));
            Assert.That(reader.SkippedOversized, Is.EqualTo(1));
            Assert.That(reader.Truncated, Is.False);
        });
    }

    [Test]
    public void GarbageWhereARecordShouldStartIsAnErrorAtAnOffsetTest()
    {
        var stream = new MemoryStream();
        Append(stream, "conversion", "http://a/", "hello");
        var junk = Encoding.ASCII.GetBytes("this is not a warc record\r\n\r\n");
        stream.Write(junk);
        stream.Position = 0;

        using var reader = new WarcReader(stream);
        var exception = Assert.Throws<WarcFormatException>(() => reader.ReadRecords().ToList());

        Assert.That(exception!.Message, Does.Contain("offset").And.Contain("WARC version line"));
    }

    [Test]
    public void MissingAndBadContentLengthAreErrorsTest()
    {
        var missing = new MemoryStream(Encoding.ASCII.GetBytes("WARC/1.0\r\nWARC-Type: conversion\r\n\r\npayload\r\n\r\n"));
        var bad = new MemoryStream(Encoding.ASCII.GetBytes("WARC/1.0\r\nWARC-Type: conversion\r\nContent-Length: -4\r\n\r\n"));

        using var reader1 = new WarcReader(missing);
        using var reader2 = new WarcReader(bad);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<WarcFormatException>(() => reader1.ReadRecords().ToList())!.Message, Does.Contain("Content-Length"));
            Assert.That(Assert.Throws<WarcFormatException>(() => reader2.ReadRecords().ToList())!.Message, Does.Contain("Content-Length"));
        });
    }

    [Test]
    public void PlainAndGzippedInputReadTheSameTest()
    {
        var plain = new MemoryStream();
        Append(plain, "conversion", "http://a/", "alpha");
        Append(plain, "conversion", "http://b/", "beta");
        var gzipped = new MemoryStream();
        using (var gz = new GZipStream(gzipped, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(plain.ToArray());

        plain.Position = 0;
        gzipped.Position = 0;
        using var reader1 = new WarcReader(plain);
        using var reader2 = new WarcReader(gzipped);

        var a = reader1.ReadRecords().Select(r => Encoding.UTF8.GetString(r.Payload.Span)).ToList();
        var b = reader2.ReadRecords().Select(r => Encoding.UTF8.GetString(r.Payload.Span)).ToList();

        Assert.That(a, Is.EqualTo(new[] { "alpha", "beta" }));
        Assert.That(b, Is.EqualTo(a));
    }

    #endregion

    #region Helpers

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    private static void Append(Stream stream, string type, string uri, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = $"WARC/1.0\r\nWARC-Type: {type}\r\nWARC-Target-URI: {uri}\r\nContent-Type: text/plain\r\nContent-Length: {bytes.Length}\r\n\r\n";
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(bytes);
        stream.Write(Encoding.ASCII.GetBytes("\r\n\r\n"));
    }

    #endregion
}
