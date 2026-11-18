namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The shards and the language models the tests read, by name.
/// <para>
/// The two shards in the repository are generated: <see cref="CrawlSynthShards"/> writes them
/// from text of the tests' own, and <c>CrawlSynthTests</c> holds the committed files to the
/// generator. No record of a real crawl is committed.
/// </para>
/// <para>
/// A test that has to see real crawl data — a product pinned on it, a ratio measured on it, an
/// agreement with the crawl's own labels — asks for one of the two crawl heads instead: the
/// first bytes of one Common Crawl WET file and of the WARC file it was converted from, fetched
/// by range when the test runs and held to a committed hash (<see cref="CorpusCrawlHeads"/>).
/// Such a test carries the category <see cref="CRAWL"/> and is ignored, by name, on a machine
/// that cannot reach the origin.
/// </para>
/// </summary>
public static class CorpusFixtures
{
    #region Constants

    /// <summary>The generated WET shard: the text of every page of the generated crawl.</summary>
    public const string WET = "generated-web.warc.wet.gz";

    /// <summary>The generated WARC shard: the first pages of the generated crawl as their sites serve them.</summary>
    public const string WARC = "generated-web.warc.gz";

    /// <summary>The head of a real Common Crawl WET file (CC-MAIN-2026-43, segment 1791170053823.21, file 00000): fetched, never committed.</summary>
    public const string CRAWL_WET = "cc-main-2026-43-00000-head.warc.wet.gz";

    /// <summary>The head of the WARC file that WET file is the conversion of: fetched, never committed.</summary>
    public const string CRAWL_WARC = "cc-main-2026-43-00000-head.warc.gz";

    /// <summary>The reference language pack (WITLID2): sixteen supported languages.</summary>
    public const string PACK = "language-pack-cc-2026-43-16.bin";

    /// <summary>The 0.1.x language model (WITLID1).</summary>
    public const string WITLID1 = "language-model-cc-2026-43-16.bin";

    /// <summary>The category of the tests that read real crawl data: they need the network, once.</summary>
    public const string CRAWL = "Crawl";

    private const string FOLDER = "Fixtures";

    #endregion

    #region Functions

    /// <summary>
    /// The path of a fixture. A crawl head is fetched and checked first; a test that asks for one
    /// on a machine that cannot have it is ignored, with the reason (on the build server it fails).
    /// </summary>
    /// <param name="name">The fixture's name.</param>
    /// <returns>The full path.</returns>
    public static string PathOf(string name)
    {
        return name is CRAWL_WET or CRAWL_WARC ? CorpusCrawlHeads.Require(name) : Path.Combine(Folder, name);
    }

    #endregion

    #region Properties

    /// <summary>The fixtures folder of the test output: the committed fixtures, copied by the build.</summary>
    public static string Folder => Path.Combine(TestContext.CurrentContext.TestDirectory, FOLDER);

    #endregion
}
