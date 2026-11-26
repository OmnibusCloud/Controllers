using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// The folder of a local run is held as a fleet run's is, before the sandbox is made: one that
/// holds the documents of another run is refused, not mixed. No module is needed.
/// </summary>
[TestFixture]
public sealed class SmtLiveLocalFolderTests
{
    #region Fields

    private string m_root = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_root);
        m_output.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task AFolderThatHoldsTheDocumentsOfAnotherRunIsRefusedBeforeAnythingRunsTest()
    {
        var set = Path.Combine(m_root, "problems.jsonl");
        File.WriteAllText(set, "{\"id\":\"a\",\"smt2\":\"(assert true)\"}\n");
        var folder = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "earlier"));

        var exit = await new SmtLiveLocal(m_output, SmtLiveClock.StartNow()).RunAsync(set, new SmtOptionsData(), null, folder, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
            Assert.That(m_output.ToString(), Does.Contain("already holds results.jsonl"));
            Assert.That(m_output.ToString(), Does.Not.Contain("module   :"), "refused before the sandbox was made");
            Assert.That(File.ReadAllBytes(Path.Combine(folder, SmtLiveDocuments.REPORT.File)), Is.EqualTo(File.ReadAllBytes(SmtLiveFixtures.Run(SmtLiveDocuments.REPORT))));
        });
    }

    #endregion
}
