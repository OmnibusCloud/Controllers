using System.IO.Compression;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Audit finding M-6: the repository held three megabytes of real crawl records as test
/// fixtures. They are gone; the shards in their place are generated, and these tests are what
/// makes that a fact and keeps it one: the committed shards are, record for record, what the
/// generator writes from the tests' own text; the fixtures folder of the repository holds a
/// pinned list of files and no other shard; every address in the shards is on a reserved domain;
/// and the list of real crawl data the tests fetch holds names, ranges and hashes, nothing of
/// the content.
/// </summary>
[TestFixture]
public sealed class CrawlSynthTests
{
    #region Constants

    /// <summary>Set (to anything) to have the generator write the two shards into the repository's fixtures folder instead of comparing them: how a change to the generator or its text reaches the committed files. Every pin of the generated shards moves with them.</summary>
    private const string WRITE_VARIABLE = "WITAI_CORPUS_WRITE_SHARDS";

    private const string TESTS = "OutWit.Controller.AI.Corpus.Tests";
    private const string FIXTURES = "Fixtures";

    /// <summary>The fixtures folder of the repository: these files and no other. The licence text is FineWeb-2's, beside the pack that carries its lists.</summary>
    private static readonly IReadOnlyList<string> COMMITTED =
    [
        CorpusCrawlHeads.LISTING, CorpusFixtures.WARC, CorpusFixtures.WET, CorpusFixtures.WITLID1, CorpusFixtures.PACK, CorpusFixtures.PACK_LICENCE,
        .. CrawlSynth.SUPPORTED.Concat(CrawlSynth.UNSUPPORTED).Select(code => $"{CrawlSynthLanguage.FOLDER}/{code}.txt")
    ];

    #endregion

    #region Provenance Tests

    [TestCase(CorpusFixtures.WET)]
    [TestCase(CorpusFixtures.WARC)]
    public void M6TheCommittedShardIsWhatTheGeneratorWritesTest(string name)
    {
        var records = name == CorpusFixtures.WET ? CrawlSynthShards.WetRecords(CorpusFixtures.Folder) : CrawlSynthShards.WarcRecords(CorpusFixtures.Folder);

        if (Environment.GetEnvironmentVariable(WRITE_VARIABLE) is { Length: > 0 })
        {
            var shard = name == CorpusFixtures.WET ? CrawlSynthShards.Wet(CorpusFixtures.Folder) : CrawlSynthShards.Warc(CorpusFixtures.Folder);
            File.WriteAllBytes(CorpusRepositoryFiles.PathOf("AI", TESTS, FIXTURES, name), shard);
            File.WriteAllBytes(CorpusFixtures.PathOf(name), shard);
            Assert.Inconclusive($"{name} written: {records.Count} records, {shard.Length} bytes. Unset {WRITE_VARIABLE}, move the pins of the generated shards and commit the files.");
        }

        var generated = records.SelectMany(record => record).ToArray();
        var committed = Unpack(File.ReadAllBytes(CorpusFixtures.PathOf(name)));

        using var reader = WarcReader.Open(CorpusFixtures.PathOf(name));
        var read = reader.ReadRecords().Count();

        Assert.Multiple(() =>
        {
            Assert.That(committed.Length, Is.EqualTo(generated.Length), "the committed shard unpacks to the generator's bytes");
            Assert.That(FirstDifference(committed, generated), Is.EqualTo(-1), "the offset of the first byte that is not the generator's (-1: none)");
            Assert.That(read, Is.EqualTo(records.Count), "and holds the generator's records, one a gzip member");
        });
    }

    [Test]
    public void M6TheGeneratorGivesTheSameCrawlEveryTimeTest()
    {
        var first = CrawlSynthShards.WetRecords(CorpusFixtures.Folder);
        var second = CrawlSynthShards.WetRecords(CorpusFixtures.Folder);

        Assert.That(second.SelectMany(record => record), Is.EqualTo(first.SelectMany(record => record)));
    }

    [Test]
    public void M6TheFixturesOfTheRepositoryAreAPinnedListTest()
    {
        var folder = CorpusRepositoryFiles.PathOf("AI", TESTS, FIXTURES);
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(files, Is.EqualTo(COMMITTED.Order(StringComparer.Ordinal)), "a file here that is not in the list is content nobody answered for: a shard must be the generator's, real crawl data is fetched and never committed");
    }

    [TestCase(CorpusFixtures.WET)]
    [TestCase(CorpusFixtures.WARC)]
    public void M6EveryAddressOfAShardIsOnAReservedDomainTest(string name)
    {
        using var reader = WarcReader.Open(CorpusFixtures.PathOf(name));
        var hosts = reader.ReadRecords().Where(record => record.TargetUri != null).Select(record => new Uri(record.TargetUri!).Host).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(hosts, Is.Not.Empty);
            Assert.That(hosts, Is.All.EndsWith(".example"), "the sites of the generated crawl do not exist");
        });
    }

    #endregion

    #region Crawl Tests

    [Test]
    public void M6TheListOfCrawlHeadsHoldsNamesRangesAndHashesOnlyTest()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(CorpusFixtures.PathOf(CorpusCrawlHeads.LISTING)));
        var root = document.RootElement;
        var heads = root.GetProperty("heads").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(root.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "note", "origin", "heads" }));
            Assert.That(root.GetProperty("origin").GetString(), Is.EqualTo("https://data.commoncrawl.org/"));
            Assert.That(heads.Select(head => head.GetProperty("name").GetString()), Is.EqualTo(new[] { CorpusFixtures.CRAWL_WET, CorpusFixtures.CRAWL_WARC }));
            foreach (var head in heads)
            {
                Assert.That(head.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "name", "path", "offset", "length", "sha256" }));
                Assert.That(head.GetProperty("path").GetString(), Does.StartWith("crawl-data/CC-MAIN-"));
                Assert.That(head.GetProperty("sha256").GetString(), Does.Match("^[0-9a-f]{64}$"));
                Assert.That(head.GetProperty("length").GetInt64(), Is.InRange(1, 4 << 20), "a head, not a file");
            }
        });
    }

    [TestCase(CorpusFixtures.CRAWL_WET, 437)]
    [TestCase(CorpusFixtures.CRAWL_WARC, 350)]
    [Category(CorpusFixtures.CRAWL)]
    public void M6ACrawlHeadIsFetchedByRangeAndIsTheBytesTheListNamesTest(string name, int records)
    {
        // Fetched, or found from an earlier run, and in either case held to its hash before the path is given.
        var path = CorpusFixtures.PathOf(name);
        var listed = CorpusCrawlHeads.Listing().Heads.Single(head => head.Name == name);

        using var reader = WarcReader.Open(path);
        var read = reader.ReadRecords().Count();

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(path).Length, Is.EqualTo(listed.Length));
            Assert.That(Path.GetFullPath(path), Does.Not.StartWith(Path.GetFullPath(CorpusRepositoryFiles.PathOf("AI", TESTS, FIXTURES))), "kept outside the committed fixtures");
            Assert.That(read, Is.EqualTo(records), "whole gzip members: the head ends where a record ends");
            Assert.That(reader.Truncated, Is.False);
        });
    }

    #endregion

    #region Tools

    private static byte[] Unpack(byte[] shard)
    {
        using var gzip = new GZipStream(new MemoryStream(shard), CompressionMode.Decompress);
        using var unpacked = new MemoryStream();
        gzip.CopyTo(unpacked);
        return unpacked.ToArray();
    }

    private static int FirstDifference(byte[] left, byte[] right)
    {
        var shared = Math.Min(left.Length, right.Length);
        for (var index = 0; index < shared; index++)
        {
            if (left[index] != right[index])
                return index;
        }

        return left.Length == right.Length ? -1 : shared;
    }

    #endregion
}
