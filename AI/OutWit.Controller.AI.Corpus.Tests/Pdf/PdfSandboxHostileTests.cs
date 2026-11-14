using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The hostile set — 41 PDFs built to hurt a parser: decompression bombs in every stream a text
/// extractor decodes, 100k-deep nesting, reference cycles, page-tree amplification, broken and
/// lying cross references, truncation, pathological fonts — through the stage's sandbox under the
/// stage's own limits. Every file ends bounded, in its expected verdict, inside this process, and
/// its product (verdict, pages, text hash, fuel) is pinned: the same on every platform the
/// workflow runs. Needs the module (<see cref="PdfTestModule"/>); the runs go in parallel (the
/// sandbox's slots keep their memory within half the machine's).
/// </summary>
[TestFixture]
public sealed class PdfSandboxHostileTests
{
    #region Constants

    /// <summary>
    /// What each file comes to under the stage's limits. Produced on win-x64 and re-checked on every
    /// platform the workflow runs; a new module, engine or limit moves them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (PdfVerdict Verdict, int Pages, string Sha256, long Fuel)> GOLDEN = new Dictionary<string, (PdfVerdict, int, string, long)>
    {
        ["amplify-form-fanout-16x6.pdf"] = (PdfVerdict.Text, 1, "2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881", 5805160),
        ["bomb-content-1g.pdf"] = (PdfVerdict.Fuel, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 80000000000),
        ["bomb-content-3g-double-flate.pdf"] = (PdfVerdict.Fuel, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 80000000000),
        ["bomb-fontfile-1g.pdf"] = (PdfVerdict.Text, 1, "07a7c19d7ddc52283ff5f68b11b42b98831af4dcaa1f7078e57d6e8852a9cb2d", 66849875757),
        ["bomb-image-1g.pdf"] = (PdfVerdict.Text, 1, "d199b210632cde3359a2a8f8509ead71e3bf4d31774773566b74dfb706138222", 19838223),
        ["bomb-lzw-1g.pdf"] = (PdfVerdict.Memory, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 2454449291),
        ["bomb-objstm-1g.pdf"] = (PdfVerdict.Fuel, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 80000000000),
        ["bomb-predictor-columns.pdf"] = (PdfVerdict.Text, 1, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 1218795),
        ["bomb-tounicode-1g.pdf"] = (PdfVerdict.Text, 1, "c87178ccdf38084caf73b6bdde0d9234343d6da3e35e02b82f7d1cd2e5f48278", 66007861453),
        ["content-5m-text-ops.pdf"] = (PdfVerdict.Text, 1, "31e9990b6983cc4133be377944b2708de692b4df3b157f12acf350ac94445a74", 11514711820),
        ["content-inline-image-no-ei.pdf"] = (PdfVerdict.Text, 1, "6db7d803e74f1ffa7d8f5adc0bf95b3e15bf4c8373fffadf546227cc6c6742cb", 212602780),
        ["cycle-form-pair.pdf"] = (PdfVerdict.Text, 1, "48e68c3501803e38d1448427ca8ef3ffdcddb5bfe1cbeb378b1264ee66f31ab9", 3505452),
        ["cycle-form-self.pdf"] = (PdfVerdict.Text, 1, "07397d633f25a7101990a75864ae03d5a3b9ac07c4ed6accbc52cbfd7d7c13b4", 3180628),
        ["cycle-length-self.pdf"] = (PdfVerdict.Text, 1, "adb03d01e0f29fe7b25c35cc9971f4c4f906315da6b0c7f883925d5460cd4d7d", 2932237),
        ["cycle-page-tree.pdf"] = (PdfVerdict.Text, 1, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 576971),
        ["cycle-parent-resources.pdf"] = (PdfVerdict.Text, 1, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 657605),
        ["cycle-self-contents.pdf"] = (PdfVerdict.Text, 1, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 880219),
        ["deep-array-100k.pdf"] = (PdfVerdict.Trap, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 10420205),
        ["deep-content-array-100k.pdf"] = (PdfVerdict.Trap, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 21789892),
        ["deep-dict-100k.pdf"] = (PdfVerdict.Trap, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 42188719),
        ["deep-q-1m.pdf"] = (PdfVerdict.Text, 1, "d81c55f49c5bb0d36bc11e3966ec4efab66f8dfefbbc1761161ca9d230e5466a", 1154485743),
        ["empty-file.pdf"] = (PdfVerdict.Unreadable, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 20015),
        ["encrypted-unknown-password.pdf"] = (PdfVerdict.Encrypted, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 944550),
        ["font-cff-subr-recursion.pdf"] = (PdfVerdict.Text, 1, "c3008a49e1aedcec3e9fc34800944ee0e3d3eff9f86573656ba129aa6f32174d", 3039119),
        ["font-truetype-garbage-tables.pdf"] = (PdfVerdict.Text, 1, "ed8f486ec15d6e7f8d400e004a2c95e6a4439cfa300364513b6416a890e5c409", 2613482),
        ["font-type3-recursive.pdf"] = (PdfVerdict.Text, 1, "61be55a8e2f6b4e172338bddf184d6dbee29c98853e0a0485ecee7f27b9af0b4", 3730738),
        ["not-a-pdf.pdf"] = (PdfVerdict.Unreadable, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 3722255),
        ["object-number-huge.pdf"] = (PdfVerdict.NoPages, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 496180),
        ["pages-count-lie.pdf"] = (PdfVerdict.Text, 1, "0b092f0a09211d03a9c188630823fc76bb41eadb960d028c8a1f00a40f07c80a", 3024353),
        ["pages-fanout-1m.pdf"] = (PdfVerdict.Fuel, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 80000000000),
        ["pages-real-20k.pdf"] = (PdfVerdict.Text, 20000, "36b602c1ce60bb34e7528dddb0658e760d768427ed6877bfbf530dca64bc365a", 8810788548),
        ["truncated-10.pdf"] = (PdfVerdict.Text, 8, "35128d1ccf19f9aa46a2f5d51b2c86117f53593fde2a55c6644e380bf0c07154", 12120382),
        ["truncated-50.pdf"] = (PdfVerdict.Text, 100, "e49f42031bc6e1ff3c4b51eb2b418ab9e117eb55715cc1a5067b19809663affa", 240908033),
        ["truncated-90.pdf"] = (PdfVerdict.Text, 100, "e7072ef2fda4523272c153e973b9a0884964b793816b09f3c8cb61fd1608b160", 99847065),
        ["truncated-99.pdf"] = (PdfVerdict.Text, 100, "e7072ef2fda4523272c153e973b9a0884964b793816b09f3c8cb61fd1608b160", 100003761),
        ["valid-100p.pdf"] = (PdfVerdict.Text, 100, "e7072ef2fda4523272c153e973b9a0884964b793816b09f3c8cb61fd1608b160", 94758968),
        ["valid-hello.pdf"] = (PdfVerdict.Text, 1, "7f625923b1e427d6127890b85a45e63b1a8aef92c5b3fc0d2a6fa27c82c43464", 3051350),
        ["xref-missing.pdf"] = (PdfVerdict.Text, 3, "2758ead27debd4079b93b2988431f0d61b87b91862088e866c553ecaa7d244d7", 6666074),
        ["xref-shifted-offsets.pdf"] = (PdfVerdict.Text, 3, "2758ead27debd4079b93b2988431f0d61b87b91862088e866c553ecaa7d244d7", 7140385),
        ["xref-startxref-garbage.pdf"] = (PdfVerdict.Text, 3, "2758ead27debd4079b93b2988431f0d61b87b91862088e866c553ecaa7d244d7", 6793422),
        ["xref-stream-size-2g.pdf"] = (PdfVerdict.Text, 1, "678381ee0edb386a8c10818304a132cc2e151d8afeab654c6f69479089cc2288", 6247802)
    };

    #endregion

    #region Fields

    private string m_work = null!;
    private Dictionary<string, (PdfExtraction Result, long Ms)> m_results = null!;
    private long m_totalMs;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var module = PdfTestModule.Require();
        var files = PdfHostileSet.Build();
        m_work = Path.Combine(Path.GetTempPath(), $"witai_corpus_hostile_{Guid.NewGuid():N}");

        using var sandbox = new PdfSandbox(module);
        var results = new (PdfExtraction Result, long Ms)[files.Count];
        var clock = Stopwatch.StartNew();
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, index =>
        {
            var watch = Stopwatch.StartNew();
            var result = sandbox.Extract(files[index].Bytes, Path.Combine(m_work, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            results[index] = (result, watch.ElapsedMilliseconds);
        });
        m_totalMs = clock.ElapsedMilliseconds;

        m_results = files.Select((file, index) => (file.Name, results[index])).ToDictionary(pair => pair.Name, pair => pair.Item2);
        foreach (var (name, (result, ms)) in m_results.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            TestContext.Progress.WriteLine($"{PdfSandboxTests.Golden(name, result)} // {ms} ms");
        TestContext.Progress.WriteLine($"hostile set: {files.Count} files in {m_totalMs} ms");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_work != null && Directory.Exists(m_work))
            Directory.Delete(m_work, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void EveryFileEndsInAVerdictTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_results, Has.Count.EqualTo(41));
            Assert.That(m_results.Values.Select(value => value.Result.Verdict), Has.None.EqualTo(PdfVerdict.SandboxUnavailable));
            Assert.That(m_results.Values.Select(value => value.Result.Fuel), Is.All.LessThanOrEqualTo(PdfStageLimits.FUEL), "nothing burns past its budget");
        });
    }

    [TestCase("bomb-content-1g.pdf", PdfVerdict.Fuel)]
    [TestCase("bomb-content-3g-double-flate.pdf", PdfVerdict.Fuel)]
    [TestCase("bomb-objstm-1g.pdf", PdfVerdict.Fuel)]
    [TestCase("pages-fanout-1m.pdf", PdfVerdict.Fuel)]
    [TestCase("bomb-lzw-1g.pdf", PdfVerdict.Memory)]
    [TestCase("deep-array-100k.pdf", PdfVerdict.Trap)]
    [TestCase("deep-dict-100k.pdf", PdfVerdict.Trap)]
    [TestCase("deep-content-array-100k.pdf", PdfVerdict.Trap)]
    [TestCase("encrypted-unknown-password.pdf", PdfVerdict.Encrypted)]
    [TestCase("object-number-huge.pdf", PdfVerdict.NoPages)]
    [TestCase("not-a-pdf.pdf", PdfVerdict.Unreadable)]
    [TestCase("empty-file.pdf", PdfVerdict.Unreadable)]
    [TestCase("bomb-fontfile-1g.pdf", PdfVerdict.Text)]
    [TestCase("bomb-tounicode-1g.pdf", PdfVerdict.Text)]
    [TestCase("bomb-image-1g.pdf", PdfVerdict.Text)]
    [TestCase("content-5m-text-ops.pdf", PdfVerdict.Text)]
    [TestCase("pages-real-20k.pdf", PdfVerdict.Text)]
    [TestCase("truncated-10.pdf", PdfVerdict.Text)]
    [TestCase("valid-100p.pdf", PdfVerdict.Text)]
    public void AFileEndsInItsVerdictTest(string name, PdfVerdict verdict)
    {
        Assert.That(m_results[name].Result.Verdict, Is.EqualTo(verdict));
    }

    [Test]
    public void ABudgetStopReportsItsWholeBudgetTest()
    {
        var stopped = m_results.Values.Where(value => value.Result.Verdict == PdfVerdict.Fuel).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.Not.Empty);
            Assert.That(stopped.Select(value => value.Result.Fuel), Is.All.EqualTo(PdfStageLimits.FUEL));
        });
    }

    [Test]
    public void TheHostileSetsProductsArePinnedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_results.Keys, Is.EquivalentTo(GOLDEN.Keys), "every file is pinned");
            foreach (var (name, (result, _)) in m_results)
            {
                if (!GOLDEN.TryGetValue(name, out var golden))
                    continue;

                Assert.That((result.Verdict, result.Pages, PdfSandboxTests.Sha256(result.Text), result.Fuel), Is.EqualTo(golden), PdfSandboxTests.Golden(name, result));
            }
        });
    }

    #endregion
}
