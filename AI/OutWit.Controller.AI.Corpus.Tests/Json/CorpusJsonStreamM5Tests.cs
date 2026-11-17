using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Json;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Json;

/// <summary>
/// Audit finding M-5, at the two documents that grow with a run and are a lab's input: a manifest
/// and a report are read from a stream, an entry at a time, and never held as text. The reader
/// gives the same answers as a parser of the whole document, whatever the size of the buffer the
/// bytes arrive in, and holds no more than its largest value.
/// </summary>
[TestFixture]
public sealed class CorpusJsonStreamM5Tests
{
    #region Tests

    [Test]
    public void M5ADocumentIsReadAnItemAtATimeTest()
    {
        var json = """{"pipeline":{"version":"v","nested":{"a":[1,2,{"b":null}]}},"sources":["a",{"url":"b","size":7},3,null,[1,2],true],"last":"x"}""";
        var properties = new List<string>();
        var items = new List<string>();

        var found = Read(json, properties, items);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(properties, Is.EqualTo(new[] { """pipeline={"version":"v","nested":{"a":[1,2,{"b":null}]}}""", "last=\"x\"" }));
            Assert.That(items, Is.EqualTo(new[] { "0=\"a\"", """1={"url":"b","size":7}""", "2=3", "3=null", "4=[1,2]", "5=true" }));
        });
    }

    [Test]
    public void M5TheAnswerDoesNotDependOnHowTheBytesArriveTest()
    {
        // A document longer than the reader's buffer, with values that cross every boundary:
        // read whole, and through streams that give 1, 7 and 4,096 bytes at a time.
        var sources = string.Join(",", Enumerable.Range(0, 5000).Select(index => index % 3 == 0
            ? $"\"https://data.commoncrawl.org/crawl-data/segment/shard-{index:D6}.warc.wet.gz\""
            : "{\"url\":\"https://data.commoncrawl.org/x/" + index + ".warc.gz\",\"size\":" + index * 1000L + ",\"note\":\"é ü \u00e9 " + new string('z', index % 97) + "\"}"));
        var json = "  {\"allowed_origins\":[\"data.commoncrawl.org\"], \"sources\":[" + sources + "] , \"pipeline\":{\"version\":\"v\"}}  ";

        var whole = (Properties: new List<string>(), Items: new List<string>());
        Read(json, whole.Properties, whole.Items);

        Assert.Multiple(() =>
        {
            Assert.That(whole.Items, Has.Count.EqualTo(5000));
            Assert.That(Encoding.UTF8.GetByteCount(json), Is.GreaterThan(256 * 1024), "several buffers long");

            foreach (var chunk in new[] { 1, 7, 4096 })
            {
                var properties = new List<string>();
                var items = new List<string>();
                using var stream = new TrickleStream(Encoding.UTF8.GetBytes(json), chunk);
                CorpusJsonStream.Read(stream, "sources", (name, value) => properties.Add($"{name}={value.GetRawText()}"), (index, item) => items.Add($"{index}={item.GetRawText()}"));

                Assert.That(properties, Is.EqualTo(whole.Properties), $"{chunk} byte(s) at a time");
                Assert.That(items, Is.EqualTo(whole.Items), $"{chunk} byte(s) at a time");
            }
        });
    }

    [Test]
    public void M5TheReaderHoldsItsLargestValueNotTheDocumentTest()
    {
        // 200,000 sources, about 20 MB of text: read with a few hundred kilobytes allocated at a time.
        using var stream = new GeneratedManifest(200_000);
        var count = 0;
        var before = GC.GetTotalMemory(forceFullCollection: true);
        long peak = 0;

        CorpusJsonStream.Read(stream, "sources", (_, _) => { }, (_, _) =>
        {
            if (++count % 20_000 == 0)
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true) - before);
        });

        TestContext.Out.WriteLine($"{count} items, {stream.Position} bytes read; at most {peak} bytes held while reading");

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(200_000));
            Assert.That(stream.Position, Is.GreaterThan(15_000_000));
            Assert.That(peak, Is.LessThan(2_000_000), "the document was never in memory");
        });
    }

    [TestCase("")]
    [TestCase("[]")]
    [TestCase("7")]
    [TestCase("""{"sources":[1,2""")]
    [TestCase("""{"sources":[1,2]}}""")]
    [TestCase("""{"sources":[1,2]} {"again":1}""")]
    [TestCase("""{"a":1,"a":2}""")]
    [TestCase("""{"sources":[1],"sources":[2]}""")]
    [TestCase("""{"sources":[1,}""")]
    [TestCase("not json")]
    public void M5WhatIsNoDocumentIsRefusedTest(string json)
    {
        Assert.That(() => Read(json, [], []), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void M5AValueLargerThanAValueMayBeIsRefusedNotHeldTest()
    {
        // One string of more than the bound: the reader stops growing its buffer at the bound.
        using var stream = new GeneratedValue(CorpusJsonStream.MAX_VALUE_BYTES * 3L);
        var before = GC.GetTotalAllocatedBytes(precise: true);

        var failure = Assert.Throws<JsonException>(() => CorpusJsonStream.Read(stream, "sources", (_, _) => { }, (_, _) => { }));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain($"larger than {CorpusJsonStream.MAX_VALUE_BYTES} bytes"));
            Assert.That(stream.Position, Is.LessThan(CorpusJsonStream.MAX_VALUE_BYTES * 2L), "the reading stopped at the bound, not at the end of the value");
            Assert.That(GC.GetTotalAllocatedBytes(precise: true) - before, Is.LessThan(8L * CorpusJsonStream.MAX_VALUE_BYTES));
        });
    }

    [Test]
    public void M5AnArrayThatIsNotThereOrIsNoArrayIsSaidTest()
    {
        var properties = new List<string>();

        Assert.Multiple(() =>
        {
            Assert.That(Read("""{"pipeline":{}}""", [], []), Is.False, "no such property");
            Assert.That(Read("""{"sources":[]}""", [], []), Is.True, "an empty array is an array");
            Assert.That(Read("""{"sources":7}""", properties, []), Is.False, "named, and no array");
            Assert.That(properties, Is.EqualTo(new[] { "sources=7" }), "its value is handed over as a property");
        });
    }

    [Test]
    public void M5CommentsAndTrailingCommasAreReadWhenAllowedTest()
    {
        var json = "﻿" + """
            // a manifest written by hand
            { "sources": [ "a", /* the second */ "b", ], "pipeline": { "version": "v", }, }
            """;
        var items = new List<string>();
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json.TrimStart('﻿'))).ToArray());

        CorpusJsonStream.Read(stream, "sources", (_, _) => { }, (index, item) => items.Add($"{index}={item.GetRawText()}"),
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        Assert.That(items, Is.EqualTo(new[] { "0=\"a\"", "1=\"b\"" }));
    }

    #endregion

    #region Tools

    private static bool Read(string json, List<string> properties, List<string> items)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return CorpusJsonStream.Read(stream, "sources", (name, value) => properties.Add($"{name}={value.GetRawText()}"), (index, item) => items.Add($"{index}={item.GetRawText()}"));
    }

    #endregion

    #region Types

    /// <summary>A manifest of any length that exists only as it is read.</summary>
    private sealed class GeneratedManifest(int sources) : Stream
    {
        private readonly MemoryStream m_pending = new();
        private int m_next = -1;
        private long m_position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => m_position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (m_pending.Position == m_pending.Length && m_next <= sources)
            {
                m_pending.SetLength(0);
                var text = m_next == -1 ? """{"pipeline":{"version":"v"},"allowed_origins":["data.commoncrawl.org"],"sources":["""
                    : m_next == sources ? "]}"
                    : $$"""{{(m_next == 0 ? "" : ",")}}{"url":"https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/1.21/wet/CC-MAIN-{{m_next:D8}}.warc.wet.gz","size":{{64_000_000 + m_next}}}""";
                m_pending.Write(Encoding.UTF8.GetBytes(text));
                m_pending.Position = 0;
                m_next++;
            }

            var read = m_pending.Read(buffer, offset, count);
            m_position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>A document whose one value is a string of a given length, made as it is read.</summary>
    private sealed class GeneratedValue(long length) : Stream
    {
        private static readonly byte[] HEAD = "{\"pipeline\":\""u8.ToArray();
        private long m_position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => m_position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            for (; read < count && m_position < HEAD.Length + length + 2; read++, m_position++)
            {
                buffer[offset + read] = m_position < HEAD.Length ? HEAD[m_position]
                    : m_position < HEAD.Length + length ? (byte)'a'
                    : m_position == HEAD.Length + length ? (byte)'"'
                    : (byte)'}';
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    #endregion
}
