using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.CalculiX.Tests.Mock;
using OutWit.Controller.CalculiX.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.CalculiX.Tests.Adapters;

/// <summary>
/// Ccx.Solve through the engine (Grid.ForEach, task serialisation, mock
/// nodes, blob transport, the fake solver), watched from the host's temp
/// folder: every solve's scratch is a scope of that folder and goes back to
/// it however the solve ends - solved, failed, cancelled, or thrown out by an
/// infrastructure failure.
/// </summary>
[TestFixture]
[NonParallelizable]
public class WitActivityAdapterCcxSolveTests
{
    #region Constants

    private const string SCRIPT = """
                                  Job:CcxSolveTest(CcxTaskCollection:tasks)
                                  {
                                      CcxResultCollection:wave = Grid.ForEach(task in tasks) => Ccx.Solve(task);
                                  }
                                  """;

    private const string SCRATCH_LABEL = "calculix";

    #endregion

    #region Fields

    private string m_root = null!;
    private CcxTestBlobService m_blobService = null!;
    private RecordingTempStorage m_temp = null!;
    private CcxTestNodesManager m_nodes = null!;
    private IWitEngine m_engine = null!;
    private string? m_previousSolverPath;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = CalculiXTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var controllersPath = CalculiXTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        var fakeCcxPath = CalculiXTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcxPath == null)
            Assert.Ignore("fake-ccx not built");

        m_previousSolverPath = Environment.GetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH);
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, fakeCcxPath);

        m_root = Path.Combine(Path.GetTempPath(), $"witcloud_ccx_solve_{Guid.NewGuid():N}");
        m_blobService = new CcxTestBlobService(Path.Combine(m_root, "blobs"));
        m_temp = new RecordingTempStorage(Path.Combine(m_root, "temp"));

        // The host's temp folder, as the client registers it: the module's
        // own fallback (the system temp) must not be what the solve uses.
        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitTempStorage>(m_temp);
            });

        m_nodes = new CcxTestNodesManager(WitEngineNodeSdk.Instance);
        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(m_nodes);
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(CcxBinaryResolver.ENV_SOLVER_PATH, m_previousSolverPath);

        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [SetUp]
    public void Reset()
    {
        m_temp.Reset();
        m_nodes.CancelAfter = null;
    }

    #endregion

    #region Tools

    private List<CcxTaskData?> Tasks(Guid deckBlobId)
    {
        return [new CcxTaskData { VariantIndex = 0, DeckBlobId = deckBlobId, NodeCount = 8, ElementCount = 1, Threads = 1 }];
    }

    private void AssertScratchGivenBack()
    {
        var created = m_temp.CreatedScopes;

        Assert.That(created, Is.Not.Empty, "the solve ran in a scope of the host's temp folder");
        Assert.That(created.All(scope => scope.StartsWith(Path.Combine(m_temp.RootPath, SCRATCH_LABEL), StringComparison.Ordinal)), Is.True,
            "under the controller's label in the host's folder - never a folder of the controller's own");
        Assert.That(m_temp.DeletedScopes, Is.EquivalentTo(created), "every scope goes back through the host's temp folder");
        Assert.That(created.Where(Directory.Exists), Is.Empty, "nothing of a solve is left behind");
    }

    #endregion

    #region Scratch Tests

    [Test]
    public async Task ASolvedDeckGivesItsScratchBackTest()
    {
        var deck = m_blobService.AddText("*HEADING\nsolved\n", "deck.inp");

        var job = m_engine.Compile(SCRIPT);
        var status = await m_engine.ScheduleAndWaitAsync(job, Tasks(deck));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        var wave = job.Variables["wave"].Value as IReadOnlyList<CcxResultData?>;
        Assert.That(wave?.Single()?.ExitCode, Is.EqualTo(0));
        AssertScratchGivenBack();
    }

    [Test]
    public async Task AFailedSolveGivesItsScratchBackTest()
    {
        var deck = m_blobService.AddText("*HEADING\nFAKE-FAIL\n", "deck.inp");

        var job = m_engine.Compile(SCRIPT);
        var status = await m_engine.ScheduleAndWaitAsync(job, Tasks(deck));

        // A nonzero exit is data: the job completes with a red row.
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        var wave = job.Variables["wave"].Value as IReadOnlyList<CcxResultData?>;
        Assert.That(wave?.Single()?.ExitCode, Is.Not.EqualTo(0));
        AssertScratchGivenBack();
    }

    [Test]
    public async Task ACancelledSolveGivesItsScratchBackTest()
    {
        // The fake solver hangs for minutes on this deck; the node's job is
        // cancelled shortly after the solve starts, as a user's cancel arrives.
        var deck = m_blobService.AddText("*HEADING\nFAKE-HANG\n", "deck.inp");
        m_nodes.CancelAfter = TimeSpan.FromMilliseconds(700);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var job = m_engine.Compile(SCRIPT);
        await m_engine.ScheduleAndWaitAsync(job, Tasks(deck));

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(60)), "the hung solve outlived its cancellation");
        var wave = job.Variables["wave"].Value as IReadOnlyList<CcxResultData?>;
        Assert.That(wave?.Where(result => result != null) ?? [], Is.Empty, "a killed solve is never harvested as a result");
        AssertScratchGivenBack();
    }

    [Test]
    public async Task AnInfrastructureFailureGivesTheScratchBackTest()
    {
        // A deck blob the node cannot fetch: the solve throws after its scope exists.
        var job = m_engine.Compile(SCRIPT);
        var status = await m_engine.ScheduleAndWaitAsync(job, Tasks(Guid.NewGuid()));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        AssertScratchGivenBack();
    }

    #endregion
}
