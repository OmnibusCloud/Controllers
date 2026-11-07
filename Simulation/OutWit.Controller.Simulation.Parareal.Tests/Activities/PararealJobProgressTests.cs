using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Simulation.Parareal.Tests.Mock;
using OutWit.Controller.Simulation.Parareal.Tests.Utils;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;
using OutWit.Math.Simulation;

namespace OutWit.Controller.Simulation.Parareal.Tests.Activities;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math
using OutWit.Math.Simulation.Model.Parareal;
using OutWit.Math.Simulation.Model.Problem;

/// <summary>
/// The job progress the server-side activities report. The SDK host has no job-level sink, so the
/// bundled script is solved through the SDK first, and each reporting activity is then replayed on
/// that real state with a processing manager that has the sink: every report must carry the running
/// job's id, the progress its state implies and the matching stage text.
/// </summary>
[TestFixture]
public class PararealJobProgressTests
{
    #region Constants

    private const string REPLAY_SCRIPT = """
                                         Job:PararealProgressReplay(PararealPlan:plan, PararealState:state, PararealResultCollection:wave)
                                         {
                                             PararealTaskCollection:tasks = Parareal.MakeTasks(plan, state);
                                             PararealState:next = Parareal.Correct(plan, state, wave);
                                             PararealTaskCollection:snapshotTasks = Parareal.MakeSnapshotTasks(plan, state);
                                             Blob:timeline = Parareal.Collect(plan, wave, state);
                                         }
                                         """;

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private SimulationTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private PararealPlanData m_plan = null!;
    private PararealStateData m_state = null!;
    private IReadOnlyList<PararealResultData?> m_snapshotWave = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        var solutionRoot = SimulationTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SimulationTestPaths.GetPararealSolveScriptPath(solutionRoot);
        if (!File.Exists(scriptPath))
            Assert.Ignore($"PararealSolve.wit not found at {scriptPath}");

        var controllersPath = SimulationTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_parareal_progresstest_{Guid.NewGuid():N}");
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

        var model = CreateHeatModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 4,
            Eps = 1e-6,
            MaxIterations = 10,
            Coarsening = 0,
            TotalTime = 0.5,
            FineStepsPerSlab = 5,
            SnapshotsPerSlab = 1
        };

        var solve = m_engine.Compile(File.ReadAllText(scriptPath));
        var status = await m_engine.ScheduleAndWaitAsync(solve, modelBlobId, options);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        m_plan = (PararealPlanData)solve.Variables["plan"].Value!;
        m_state = (PararealStateData)solve.Variables["state"].Value!;
        m_snapshotWave = (IReadOnlyList<PararealResultData?>)solve.Variables["wave"].Value!;
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
    public async Task FirstIterationClaimsTheBarTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();
        var roundZero = new PararealStateData
        {
            Round = 0,
            Scale = m_state.Scale,
            Eps = m_state.Eps,
            StateBlobIds = m_state.StateBlobIds
        };

        await ReplayAsync("WitActivityPararealMakeTasks", manager, jobId, roundZero, m_snapshotWave);

        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, 0.0, (string?)"iteration 1: propagating 4 time slabs") }));
    }

    [Test]
    public async Task LaterIterationsAreNotReportedByMakeTasksTest()
    {
        var manager = new JobProgressRecordingManager();

        await ReplayAsync("WitActivityPararealMakeTasks", manager, Guid.NewGuid(), m_state, m_snapshotWave);

        Assert.That(manager.Reports, Is.Empty);
    }

    [Test]
    public async Task CorrectReportsTheIterationItProducedTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();

        var replay = await ReplayAsync("WitActivityPararealCorrect", manager, jobId, m_state, ActiveWave());

        var next = (PararealStateData)replay.Variables["next"].Value!;
        Assert.That(next.Round, Is.EqualTo(m_state.Round + 1));
        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, PararealProgress.AfterIteration(m_plan, next), (string?)PararealProgress.DescribeIteration(next)) }));
    }

    [Test]
    public async Task SnapshotPassAndCollectionCloseTheBarTest()
    {
        var jobId = Guid.NewGuid();
        var manager = new JobProgressRecordingManager();

        await ReplayAsync("WitActivityPararealMakeSnapshotTasks", manager, jobId, m_state, m_snapshotWave);
        await ReplayAsync("WitActivityPararealCollect", manager, jobId, m_state, m_snapshotWave);

        Assert.That(PararealProgress.IsConverged(m_state), Is.True);
        Assert.That(manager.Reports, Is.EqualTo(new[]
        {
            (jobId, PararealProgress.BeforeSnapshotPass(m_state), (string?)"snapshot pass: recomputing 4 time slabs"),
            (jobId, 1.0, (string?)"timeline collected")
        }));
    }

    [Test]
    public async Task HostWithoutTheSinkStillSolvesTest()
    {
        var manager = new ProcessingManagerWithoutJobProgress();

        await ReplayAsync("WitActivityPararealMakeTasks", manager, Guid.NewGuid(), m_state, m_snapshotWave);
        await ReplayAsync("WitActivityPararealCorrect", manager, Guid.NewGuid(), m_state, ActiveWave());
        await ReplayAsync("WitActivityPararealMakeSnapshotTasks", manager, Guid.NewGuid(), m_state, m_snapshotWave);
        await ReplayAsync("WitActivityPararealCollect", manager, Guid.NewGuid(), m_state, m_snapshotWave);
    }

    #endregion

    #region Tools

    // The slabs Parareal.Correct expects for the state's round: the snapshot wave covers them all.
    private List<PararealResultData?> ActiveWave()
    {
        var frontier = Math.Min(m_state.Round, m_plan.Slabs - 1);
        return [.. m_snapshotWave.Where(result => result!.SlabIndex >= frontier)];
    }

    private async Task<IWitJob> ReplayAsync(string activityTypeName, IWitProcessingManager manager, Guid jobId, PararealStateData state, IReadOnlyList<PararealResultData?> wave)
    {
        var replay = m_engine.Compile(REPLAY_SCRIPT);
        replay.UpdateParameters(m_plan, state, wave);

        var activity = replay.Activities.Single(candidate => candidate.GetType().Name == activityTypeName);

        // The adapter is internal to the module: build it from the assembly the engine loaded the activity from.
        var adapterTypeName = $"OutWit.Controller.Simulation.Parareal.Adapters.{activityTypeName.Replace("WitActivity", "WitActivityAdapter")}";
        var adapterType = activity.GetType().Assembly.GetType(adapterTypeName, throwOnError: true)!;
        var adapter = adapterType.GetConstructors().Single().GetParameters().Length == 3
            ? Activator.CreateInstance(adapterType, manager, m_blobService, NullLogger.Instance)
            : Activator.CreateInstance(adapterType, manager, NullLogger.Instance);

        var status = await ((IWitProcessingAdapter)adapter!).Process(Guid.NewGuid(), jobId, activity, null, replay.Variables, false);
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        return replay;
    }

    private static SimulationModelDefinition CreateHeatModel(int n)
    {
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition
        {
            Nx = n,
            Ny = n,
            Hx = h,
            Hy = h
        };

        var initial = new double[model.NodeCount];
        for (var node = 0; node < initial.Length; node++)
        {
            var x = node % n * h;
            var y = node / n * h;
            initial[node] = Math.Sin(Math.PI * x) * Math.Sin(Math.PI * y);
        }

        model.InitialPerNode = initial;
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMin, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMax, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMin, SimulationBcKind.Dirichlet, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMax, SimulationBcKind.Dirichlet, 0));
        return model;
    }

    #endregion
}
