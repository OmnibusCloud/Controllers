using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Simulation.Schwarz.Tests.Mock;
using OutWit.Controller.Simulation.Schwarz.Tests.Utils;
using OutWit.Controller.Simulation.Schwarz.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;
using OutWit.Math.Simulation;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Activities;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math
using OutWit.Math.Simulation.Model.Problem;
using OutWit.Math.Simulation.Model.Schwarz;

/// <summary>
/// The job progress the server-side activities report. The SDK host has no job-level sink, so the
/// bundled script is solved through the SDK first, and each reporting activity is then replayed on
/// that real state with a processing manager that has the sink: every report must carry the running
/// job's id, the progress its state implies and the matching stage text.
/// </summary>
[TestFixture]
public class SchwarzJobProgressTests
{
    #region Constants

    private const string REPLAY_SCRIPT = """
                                         Job:SchwarzProgressReplay(SchwarzPlan:plan, SchwarzRound:state, SchwarzResultCollection:wave)
                                         {
                                             SchwarzTaskCollection:tasks = Schwarz.MakeTasks(plan, state);
                                             SchwarzRound:next = Schwarz.Advance(plan, state, wave);
                                             SchwarzTaskCollection:finalTasks = Schwarz.MakeFinalTasks(plan, state);
                                             Blob:field = Schwarz.Assemble(plan, wave, state);
                                         }
                                         """;

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private SimulationTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private SchwarzPlanData m_plan = null!;
    private SchwarzRoundData m_state = null!;
    private IReadOnlyList<SchwarzResultData?> m_finalWave = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        var solutionRoot = SimulationTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SimulationTestPaths.GetSchwarzSolveScriptPath(solutionRoot);
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SchwarzSolve.wit not found at {scriptPath}");

        var controllersPath = SimulationTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_simulation_progresstest_{Guid.NewGuid():N}");
        m_blobService = new SimulationTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new SimulationTestNodesManager(WitEngineNodeSdk.Instance));
            });

        var model = CreateMms2dModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new SchwarzOptionsData { Parts = 3, Overlap = 2, Eps = 1e-8, MaxRounds = 500 };

        var solve = m_engine.Compile(File.ReadAllText(scriptPath));
        var status = await m_engine.ScheduleAndWaitAsync(solve, modelBlobId, options);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        m_plan = (SchwarzPlanData)solve.Variables["plan"].Value!;
        m_state = (SchwarzRoundData)solve.Variables["state"].Value!;
        m_finalWave = (IReadOnlyList<SchwarzResultData?>)solve.Variables["wave"].Value!;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Report Tests

    [Test]
    public async Task FirstRoundClaimsTheBarTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();
        var roundZero = new SchwarzRoundData { Round = 0, Eps = m_plan.Eps };

        await ReplayAsync("WitActivitySchwarzMakeTasks", manager, jobId, roundZero);

        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, 0.0, (string?)"round 1: factorizing 3 subdomains") }));
    }

    [Test]
    public async Task LaterRoundsAreNotReportedByMakeTasksTest()
    {
        var manager = new JobProgressRecordingManager();

        await ReplayAsync("WitActivitySchwarzMakeTasks", manager, Guid.NewGuid(), m_state);

        Assert.That(manager.Reports, Is.Empty);
    }

    [Test]
    public async Task AdvanceReportsTheRoundItProducedTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();

        var replay = await ReplayAsync("WitActivitySchwarzAdvance", manager, jobId, m_state);

        var next = (SchwarzRoundData)replay.Variables["next"].Value!;
        Assert.That(next.Round, Is.EqualTo(m_state.Round + 1));
        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, SchwarzProgress.AfterRound(next), (string?)SchwarzProgress.DescribeRound(next)) }));
    }

    [Test]
    public async Task FinalPassAndAssemblyCloseTheBarTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();

        await ReplayAsync("WitActivitySchwarzMakeFinalTasks", manager, jobId, m_state);
        await ReplayAsync("WitActivitySchwarzAssemble", manager, jobId, m_state);

        Assert.That(SchwarzProgress.IsConverged(m_state), Is.True);
        Assert.That(manager.Reports, Is.EqualTo(new[]
        {
            (jobId, SchwarzProgress.BeforeFinalPass(m_state), (string?)"final pass: collecting the field from 3 subdomains"),
            (jobId, 1.0, (string?)"field assembled")
        }));
    }

    [Test]
    public async Task HostWithoutTheSinkStillSolvesTest()
    {
        foreach (var activity in new[] { "WitActivitySchwarzMakeTasks", "WitActivitySchwarzAdvance", "WitActivitySchwarzMakeFinalTasks", "WitActivitySchwarzAssemble" })
            await ReplayAsync(activity, new ProcessingManagerWithoutJobProgress(), Guid.NewGuid(), m_state);
    }

    #endregion

    #region Tools

    private async Task<IWitJob> ReplayAsync(string activityTypeName, IWitProcessingManager manager, Guid jobId, SchwarzRoundData state)
    {
        var replay = m_engine.Compile(REPLAY_SCRIPT);
        replay.UpdateParameters(m_plan, state, m_finalWave);

        var activity = replay.Activities.Single(candidate => candidate.GetType().Name == activityTypeName);

        // The adapter is internal to the module: build it from the assembly the engine loaded the activity from.
        var adapterTypeName = $"OutWit.Controller.Simulation.Schwarz.Adapters.{activityTypeName.Replace("WitActivity", "WitActivityAdapter")}";
        var adapterType = activity.GetType().Assembly.GetType(adapterTypeName, throwOnError: true)!;
        var adapter = adapterType.GetConstructors().Single().GetParameters().Length == 3
            ? Activator.CreateInstance(adapterType, manager, m_blobService, NullLogger.Instance)
            : Activator.CreateInstance(adapterType, manager, NullLogger.Instance);

        var status = await ((IWitProcessingAdapter)adapter!).Process(Guid.NewGuid(), jobId, activity, null, replay.Variables, false);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        return replay;
    }

    private static SimulationModelDefinition CreateMms2dModel(int n)
    {
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition
        {
            Nx = n,
            Ny = n,
            Hx = h,
            Hy = h
        };

        var source = new double[model.NodeCount];
        for (var node = 0; node < source.Length; node++)
        {
            var x = node % n * h;
            var y = node / n * h;
            source[node] = 2 * Math.PI * Math.PI * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * y);
        }

        model.SourcePerNode = source;
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMin, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMax, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMin, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMax, SimulationBcKind.Dirichlet, 0));
        return model;
    }

    #endregion
}
