using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// How <c>run</c> and <c>fetch</c> end when something goes wrong around a job: the folder is made
/// and held to be free before anything is sent, a finished job's documents are written before
/// anything that can fail after it, and whatever stops the tool says what is left on the server
/// and how to fetch it.
/// </summary>
[TestFixture]
public sealed class SmtLiveRunEndTests
{
    #region Fields

    private string m_root = null!;
    private string m_documents = null!;
    private string m_set = null!;
    private string m_out = null!;
    private SmtLiveServerFake m_server = null!;
    private StringWriter m_output = null!;
    private Guid m_job;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = SmtLiveFixtures.NewFolder();
        m_documents = SmtLiveFixtures.CopyRun(Path.Combine(m_root, "server"));
        m_set = Path.Combine(m_root, "problems.jsonl");
        m_out = Path.Combine(m_root, "run");
        File.WriteAllText(m_set, "{\"id\":\"a\",\"smt2\":\"(declare-const x Int)(assert (> x 1))\"}\n");
        m_server = new SmtLiveServerFake();
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

    #region Folder Tests

    [Test]
    public async Task AFolderThatCannotBeMadeIsRefusedBeforeTheUploadTest()
    {
        File.WriteAllText(m_out, "a file where the folder would be");
        Answer();

        var exit = await Run(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
            Assert.That(m_server.Uploaded, Is.Empty, "nothing was uploaded");
            Assert.That(m_server.Submitted, Is.Empty, "nothing was submitted");
            Assert.That(m_output.ToString(), Does.Contain("cannot be written"));
        });
    }

    [Test]
    public async Task AFolderThatHoldsTheDocumentsOfAnotherRunIsRefusedBeforeTheUploadTest()
    {
        Directory.CreateDirectory(m_out);
        File.WriteAllText(Path.Combine(m_out, SmtLiveDocuments.REPORT.File), "{}");
        Answer();

        var exit = await Run(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
            Assert.That(m_server.Submitted, Is.Empty);
            Assert.That(m_output.ToString(), Does.Contain("already holds report.json"));
            Assert.That(File.ReadAllText(Path.Combine(m_out, SmtLiveDocuments.REPORT.File)), Is.EqualTo("{}"), "nothing of it was replaced");
        });
    }

    #endregion

    #region Download Tests

    [Test]
    public async Task ADownloadThatFailsLeavesTheOtherDocumentsAndSaysHowToFetchThemTest()
    {
        var blobs = Answer();
        m_server.Failing.Add(blobs[SmtLiveDocuments.INTEGRITY.Variable]);

        var exit = await Run(CancellationToken.None);

        var output = m_output.ToString();
        var job = Job();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.ERROR));
            Assert.That(File.Exists(Path.Combine(m_out, SmtLiveDocuments.RESULTS.File)), Is.True, "the documents before it came");
            Assert.That(File.Exists(Path.Combine(m_out, SmtLiveDocuments.PROVENANCE.File)), Is.True, "and the ones after it");
            Assert.That(output, Does.Contain("integrity.json   COULD NOT BE DOWNLOADED"));
            Assert.That(output, Does.Contain($"smt-live fetch {job}"));
        });
    }

    [Test]
    public async Task OnlyTheProvenanceMissingIsSaidByItsNameTest()
    {
        Answer(SmtLiveDocuments.PROVENANCE);

        var exit = await Run(CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.FAILED));
        Assert.That(m_output.ToString(), Does.Contain("result   : FAILED: provenance.json did not come"));
    }

    #endregion

    #region Stop Tests

    [Test]
    public async Task CtrlCWhileWaitingSaysTheJobGoesOnAndHowToFetchItTest()
    {
        m_server.Jobs = (_, _) => Track(SmtLiveJobFake.Running());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var exit = await Run(cancel.Token);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.STOPPED));
            Assert.That(output, Does.Contain($"the job {Job()} goes on on the server"));
            Assert.That(output, Does.Contain($"smt-live fetch {Job()}"));
            Assert.That(Directory.Exists(m_out), Is.False, "a folder that got nothing is not left behind");
        });
    }

    [Test]
    public async Task AWaitThatLosesTheServerSaysTheJobGoesOnAndHowToFetchItTest()
    {
        m_server.Jobs = (_, _) => Track(SmtLiveJobFake.Lost(new IOException("the server did not answer")));

        var exit = await Run(CancellationToken.None);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.ERROR));
            Assert.That(output, Does.Contain("the server did not answer"));
            Assert.That(output, Does.Contain($"the job {Job()} goes on on the server"));
            Assert.That(output, Does.Contain($"smt-live fetch {Job()}"));
        });
    }

    #endregion

    #region Fetch Tests

    [Test]
    public async Task FetchDownloadsChecksAndSummarisesAFinishedJobByItsIdTest()
    {
        var job = SmtLiveJobFake.Completed(Values());
        m_server.Existing[job.Id] = job;

        var exit = await new SmtLiveRun(m_server, m_output).FetchAsync(job.Id, m_out, CancellationToken.None);

        var output = m_output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(m_server.Uploaded, Is.Empty, "nothing is uploaded");
            Assert.That(m_server.Submitted, Is.Empty, "nothing is submitted");
            foreach (var document in SmtLiveDocuments.RUN)
                Assert.That(File.ReadAllBytes(Path.Combine(m_out, document.File)), Is.EqualTo(File.ReadAllBytes(Path.Combine(m_documents, document.File))), document.File);

            Assert.That(File.Exists(Path.Combine(m_out, SmtLiveFleetFacts.FILE)), Is.False, "a fetch measures no run");
            Assert.That(output, Does.Contain("result   : OK"));
        });
    }

    [Test]
    public async Task FetchRefusesAJobOfAnotherScriptTest()
    {
        var job = SmtLiveJobFake.Completed(Values());
        job.Script = "SmtPreflight";
        m_server.Existing[job.Id] = job;

        var exit = await new SmtLiveRun(m_server, m_output).FetchAsync(job.Id, m_out, CancellationToken.None);

        Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.USAGE));
        Assert.That(m_output.ToString(), Does.Contain("runs SmtPreflight, not SmtRun"));
    }

    #endregion

    #region Tools

    private Task<int> Run(CancellationToken ct)
    {
        return new SmtLiveRun(m_server, m_output).RunAsync(m_set, new SmtOptionsData(), m_out, ct);
    }

    /// <summary>The job the run submitted.</summary>
    private Guid Job()
    {
        return m_job;
    }

    private SmtLiveJobFake Track(SmtLiveJobFake job)
    {
        m_job = job.Id;
        return job;
    }

    /// <summary>The job answers with the blobs of the documents in the server folder, but the ones left out; the blobs by variable.</summary>
    private Dictionary<string, Guid> Answer(params SmtLiveDocument[] without)
    {
        var values = Values(without);
        m_server.Jobs = (_, _) => Track(SmtLiveJobFake.Completed(values));

        return values.ToDictionary(pair => pair.Key, pair => ((Guid?)pair.Value)!.Value);
    }

    private Dictionary<string, object?> Values(params SmtLiveDocument[] without)
    {
        var values = new Dictionary<string, object?>();
        foreach (var document in SmtLiveDocuments.RUN.Except(without))
            values[document.Variable] = (Guid?)m_server.AddFile(Path.Combine(m_documents, document.File));

        return values;
    }

    #endregion
}
