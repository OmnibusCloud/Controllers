using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Benchmark;

/// <summary>
/// The reference shard of the node benchmark: a WET file written the way Common Crawl writes
/// them (one gzip member per record) from synthetic documents — a pseudo-vocabulary built from
/// syllables, word choice skewed like natural text, sentences and paragraphs, with a share of
/// menu-like and too-short pages so the drop paths run too. Everything comes from one seeded
/// generator of its own (not <see cref="Random"/>, whose algorithm is not a contract), so the
/// documents — and therefore the product the pipeline makes of them — are the same on every
/// node; the benchmark checks the product's hash against <see cref="CorpusBenchmark.EXPECTED_CONTENT_SHA256"/>.
/// </summary>
public static class CorpusBenchmarkShard
{
    #region Constants

    /// <summary>Documents in the reference shard.</summary>
    public const int DOCUMENTS = 400;

    /// <summary>Words in a regular document.</summary>
    public const int WORDS_PER_DOCUMENT = 300;

    private const int VOCABULARY = 6000;
    private const ulong SEED = 0x5EED_C0DE_2026_1108UL;

    private static readonly string[] ONSETS = ["b", "c", "d", "f", "g", "h", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "st", "tr", "pl", "gr", "sh", "ch"];
    private static readonly string[] NUCLEI = ["a", "e", "i", "o", "u", "ai", "ea", "ou", "io"];
    private static readonly string[] CODAS = ["", "", "", "n", "r", "s", "t", "l", "nd", "st"];

    private static readonly Lazy<byte[]> BYTES = new(Build);
    private static readonly Lazy<long> TEXT_BYTES = new(() => Documents().Sum(document => (long)Encoding.UTF8.GetByteCount(document.Text)));

    #endregion

    #region Functions

    /// <summary>The documents of the reference shard, in order.</summary>
    /// <returns>URL and text of each document.</returns>
    public static IReadOnlyList<(string Url, string Text)> Documents()
    {
        var random = new SplitMix(SEED);
        var vocabulary = Vocabulary(random);
        var documents = new List<(string, string)>(DOCUMENTS);

        for (var index = 0; index < DOCUMENTS; index++)
        {
            var url = $"https://benchmark.invalid/{index:D4}";
            var kind = index % 10;
            var text = kind switch
            {
                7 => Menu(random, vocabulary),
                9 => Prose(random, vocabulary, 25),
                _ => Prose(random, vocabulary, WORDS_PER_DOCUMENT)
            };
            documents.Add((url, text));
        }

        return documents;
    }

    /// <summary>The reference shard as gzip WET bytes (built once per process).</summary>
    public static byte[] Bytes => BYTES.Value;

    /// <summary>UTF-8 bytes of the documents' text: the workload's size, independent of the gzip level.</summary>
    public static long TextBytes => TEXT_BYTES.Value;

    private static byte[] Build()
    {
        using var output = new MemoryStream();
        var number = 0;
        foreach (var (url, text) in Documents())
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var header = Encoding.ASCII.GetBytes(
                "WARC/1.0\r\n" +
                "WARC-Type: conversion\r\n" +
                $"WARC-Target-URI: {url}\r\n" +
                "WARC-Date: 2026-11-08T00:00:00Z\r\n" +
                $"WARC-Record-ID: <urn:uuid:00000000-0000-4000-8000-{number++:D12}>\r\n" +
                "Content-Type: text/plain\r\n" +
                $"Content-Length: {payload.Length}\r\n" +
                "\r\n");

            using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            gzip.Write(header);
            gzip.Write(payload);
            gzip.Write("\r\n\r\n"u8);
        }

        return output.ToArray();
    }

    private static string[] Vocabulary(SplitMix random)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var builder = new StringBuilder();
        while (words.Count < VOCABULARY)
        {
            builder.Clear();
            var syllables = 1 + random.Next(3);
            for (var s = 0; s < syllables; s++)
            {
                builder.Append(ONSETS[random.Next(ONSETS.Length)]);
                builder.Append(NUCLEI[random.Next(NUCLEI.Length)]);
                builder.Append(CODAS[random.Next(CODAS.Length)]);
            }

            words.Add(builder.ToString());
        }

        return [.. words];
    }

    private static string Word(SplitMix random, string[] vocabulary)
    {
        // Squaring a uniform draw skews the choice towards the head of the list, the way a few
        // words carry most of a natural text.
        var u = random.NextDouble();
        return vocabulary[(int)(u * u * vocabulary.Length)];
    }

    private static string Prose(SplitMix random, string[] vocabulary, int words)
    {
        var builder = new StringBuilder(words * 8);
        var inSentence = 0;
        var sentenceLength = 8 + random.Next(10);
        for (var i = 0; i < words; i++)
        {
            var word = Word(random, vocabulary);
            builder.Append(inSentence == 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word);
            inSentence++;
            if (inSentence >= sentenceLength || i == words - 1)
            {
                builder.Append(random.Next(5) == 0 ? ".\n" : ". ");
                inSentence = 0;
                sentenceLength = 8 + random.Next(10);
            }
            else
                builder.Append(random.Next(12) == 0 ? ", " : " ");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Menu(SplitMix random, string[] vocabulary)
    {
        // A navigation page: short repeated lines, the shape the repetition rules drop.
        var items = Enumerable.Range(0, 12).Select(_ => Word(random, vocabulary) + " " + Word(random, vocabulary)).ToArray();
        var builder = new StringBuilder();
        for (var line = 0; line < 60; line++)
            builder.Append(items[line % items.Length]).Append('\n');
        return builder.ToString().TrimEnd();
    }

    #endregion

    #region Types

    /// <summary>splitmix64: a fixed, documented generator, the same bits on every runtime.</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong m_state = seed;

        public ulong NextUInt64()
        {
            var z = m_state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int maxExclusive)
        {
            return (int)(NextUInt64() % (ulong)maxExclusive);
        }

        public double NextDouble()
        {
            return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        }
    }

    #endregion
}
