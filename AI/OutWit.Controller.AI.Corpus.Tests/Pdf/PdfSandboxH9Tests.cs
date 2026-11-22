using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// Audit finding H-9: the same PDF at the same fuel took 6.4 to 99.5 s, and in the slow runs the
/// process sat idle for about 90 s. The 90 s were the deletion of the PDF's input file on the
/// run's own path: a machine that inspects new files holds a delete of a document it is still
/// inspecting, and a document whose one stream inflates to a gigabyte keeps it busy that long
/// (<see cref="PdfInputFileDiagnosticsTests"/> shows it without a sandbox). A run now has a folder
/// of its own and nothing on its path deletes it.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PdfSandboxH9Tests
{
    #region Fields

    private string m_work = null!;
    private PdfSandbox m_sandbox = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_work = Path.Combine(Path.GetTempPath(), $"witai_corpus_h9_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_work);
        m_sandbox = new PdfSandbox(PdfTestModule.Require());
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_sandbox.Dispose();
        CorpusTestFolders.Delete(m_work);
    }

    #endregion

    #region Tests

    [Test]
    public void H9NothingOnARunsPathDeletesItsInputTest()
    {
        // The run's folder is handed over, whole, to whoever takes it away; here nobody does, as
        // on a machine whose file system holds every delete. The runs neither wait nor change.
        var work = Path.Combine(m_work, "kept");
        var handed = new List<string>();
        using var sandbox = new PdfSandbox(PdfTestModule.Require()) { Discard = handed.Add };
        var pdf = PdfSynth.Contract().Single(file => file.Name == "three-pages.pdf").Bytes;

        var first = sandbox.Extract(pdf, work);
        var second = sandbox.Extract(pdf, work);

        Assert.Multiple(() =>
        {
            Assert.That(handed, Has.Count.EqualTo(2));
            Assert.That(handed.Distinct().Count(), Is.EqualTo(2), "a folder of its own for every run");
            Assert.That(handed, Is.All.StartWith(work), "inside the caller's folder");
            Assert.That(handed.Select(folder => File.Exists(Path.Combine(folder, "in", "doc.pdf"))), Is.All.True, "the input was not deleted on the run's path");
            Assert.That(handed.Select(folder => Hash(File.ReadAllBytes(Path.Combine(folder, "in", "doc.pdf")))), Is.All.EqualTo(Hash(pdf)));
            Assert.That(first.Verdict, Is.EqualTo(PdfVerdict.Text));
            Assert.That((second.Verdict, second.Text, second.Pages, second.Fuel), Is.EqualTo((first.Verdict, first.Text, first.Pages, first.Fuel)), "what a run left changes nothing for the next");
        });
    }

    [Test]
    public void H9ADeleteThatWaitsHoldsNobodyTest()
    {
        // A deletion that waits (the file system's 90 s) and a folder handed over behind it: the
        // one who hands over goes on at once, and the folder goes when its turn comes.
        Assert.That(ShardCleanup.Drain(TimeSpan.FromMinutes(2)), Is.True, "nothing pending from the tests before");
        var folder = Path.Combine(m_work, "handed-over");
        Directory.CreateDirectory(Path.Combine(folder, "in"));
        File.WriteAllText(Path.Combine(folder, "in", "doc.pdf"), "a document");
        using var held = new ManualResetEventSlim(false);

        var clock = Stopwatch.StartNew();
        ShardCleanup.Schedule(() => held.Wait(TimeSpan.FromMinutes(1)));
        ShardCleanup.Schedule(folder);
        var scheduled = clock.Elapsed;
        var pending = ShardCleanup.Pending;
        var drainedWhileHeld = ShardCleanup.Drain(TimeSpan.FromMilliseconds(200));
        var thereWhileHeld = Directory.Exists(folder);

        held.Set();
        var drained = ShardCleanup.Drain(TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(scheduled, Is.LessThan(TimeSpan.FromSeconds(1)), "handing over waits for nothing");
            Assert.That(pending, Is.EqualTo(2));
            Assert.That(drainedWhileHeld, Is.False);
            Assert.That(thereWhileHeld, Is.True, "one after another: the folder waits its turn, not the shard");
            Assert.That(drained, Is.True);
            Assert.That(Directory.Exists(folder), Is.False);
            Assert.That(ShardCleanup.Pending, Is.Zero);
        });
    }

    [Test]
    public void H9ADeletionThatThrowsIsDroppedAndTheNextOneRunsTest()
    {
        var folder = Path.Combine(m_work, "after-a-throw");
        Directory.CreateDirectory(folder);

        ShardCleanup.Schedule(() => throw new UnauthorizedAccessException("held by another process"));
        ShardCleanup.Schedule(folder);

        Assert.Multiple(() =>
        {
            Assert.That(ShardCleanup.Drain(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(Directory.Exists(folder), Is.False);
        });
    }

    [Test]
    public void H9ARunsTimeIsItsFuelsTimeEveryTimeTest()
    {
        // The audit's series: one document that ends at the fuel budget, several times in a row
        // (91.9, 6.4, 97.8, 6.6, 6.6 s). The bytes are new each time, so that nothing answers from
        // what a machine remembers of a file it has seen. Every run spends the same fuel, and no
        // run takes many times the quickest one's time (the slow ones took fifteen times).
        var bomb = PdfHostileSet.Build().First(entry => entry.Name == "bomb-content-1g.pdf").Bytes;
        var work = Path.Combine(m_work, "series");
        var times = new List<double>();
        var runs = new List<(PdfVerdict Verdict, long Fuel)>();

        for (var run = 0; run < 3; run++)
        {
            var pdf = bomb.Concat(Encoding.ASCII.GetBytes($"\n% {Guid.NewGuid():N}\n")).ToArray();
            var clock = Stopwatch.StartNew();
            var result = m_sandbox.Extract(pdf, work);
            times.Add(clock.Elapsed.TotalSeconds);
            runs.Add((result.Verdict, result.Fuel));
        }

        TestContext.Out.WriteLine($"H9| three runs of a document that ends at the fuel budget: {string.Join(", ", times.Select(time => time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))} s");

        Assert.Multiple(() =>
        {
            Assert.That(runs, Is.All.EqualTo((PdfVerdict.Fuel, PdfStageLimits.FUEL)));
            Assert.That(times.Max(), Is.LessThan(Math.Max(4 * times.Min(), times.Min() + 20)), "a run's time is its fuel's");
        });
    }

    #endregion

    #region Tools

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
