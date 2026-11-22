using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The clock gives no verdict (rule R-7): a PDF the wall-clock backstop stopped is a fault of the
/// machine, never a verdict about the document. Where the clock fires decides how the engine
/// reports it: inside the guest's own code as the trap of the deadline, inside a call that passes
/// through the host as an error of that call whose cause is the deadline. Until 0.6.0 the second
/// was read as an error of the document (<see cref="PdfVerdict.Error"/>), so a PDF the clock
/// stopped became a dropped document on some runs: 2 to 3 runs in 60 on a desktop, one in the
/// fleet's build server run that found it.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PdfSandboxClockTests
{
    #region Constants

    /// <summary>Runs under a backstop of one millisecond: enough for the clock to land in a host call several times.</summary>
    private const int RUNS = 200;

    #endregion

    #region Fields

    private string m_work = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_work = Path.Combine(Path.GetTempPath(), $"witai_corpus_clock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_work);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_work);
    }

    #endregion

    #region Tests

    [Test]
    public void R7TheClockIsTheClockWhereverItFiresTest()
    {
        // A PDF that runs for seconds, a backstop of one millisecond: every run ends by the clock,
        // at a moment the machine decides. Each one must say so, and none may pass for a verdict.
        using var sandbox = new PdfSandbox(PdfTestModule.Require(), PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES, wallMs: 1);
        var pdf = PdfHostileSet.Build().First(entry => entry.Name == "pages-fanout-1m.pdf").Bytes;
        var verdicts = new List<string>();

        for (var run = 0; run < RUNS; run++)
        {
            try
            {
                var extraction = sandbox.Extract(pdf, m_work);
                verdicts.Add($"run {run}: {extraction.Verdict} after {extraction.Fuel} fuel");
            }
            catch (PdfSandboxException exception) when (exception.Code == CorpusResultCodes.FAULT_PDF_BACKSTOP)
            {
                // the clock, said as the clock
            }
        }

        Assert.That(verdicts, Is.Empty, "a run the clock stopped became a verdict about the document");
    }

    #endregion
}
