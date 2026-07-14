using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Schwarz;
using OutWit.Controller.Simulation.Schwarz.Tests.Mock;
using OutWit.Controller.Simulation.Schwarz.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Activities;

/// <summary>
/// Fault injection: a node dying mid-wave must not fail the job —
/// GridReassignment re-packs its tasks onto the survivors, and because
/// SolveSubdomain is a pure function over immutable blobs, the final
/// field is bitwise-identical to the healthy run.
/// </summary>
[TestFixture]
public class SchwarzFaultToleranceTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private SimulationTestBlobService m_blobService = null!;
    private string m_schwarzScript = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SimulationTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SimulationTestPaths.GetSchwarzSolveScriptPath(solutionRoot);
        if (!File.Exists(scriptPath))
            Assert.Ignore($"SchwarzSolve.wit not found at {scriptPath}");

        m_schwarzScript = File.ReadAllText(scriptPath);

        var controllersPath = SimulationTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_schwarz_faulttest_{Guid.NewGuid():N}");
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
                services.AddSingleton<IWitNodesManager>(new FaultInjectionNodesManager(WitEngineNodeSdk.Instance, strikes: 1));
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Fault Tests

    [Test]
    public async Task NodeFailureMidWaveRecoversBitwiseTest()
    {
        var model = CreateMms2dModel(25);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new SchwarzOptionsData
        {
            Parts = 4,
            Overlap = 3,
            Eps = 1e-10,
            MaxRounds = 500
        };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed),
            "a single node failure must be absorbed by reassignment, not fail the job");

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var state = job.Variables["state"].Value as SchwarzRoundData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(report.Rounds));

        var fieldBlobId = job.Variables["field"].Value as Guid?;
        Assert.That(fieldBlobId, Is.Not.Null);

        var field = SimulationFieldSlice.FromBlobBytes(
            await File.ReadAllBytesAsync(m_blobService.GetStoredPath(fieldBlobId!.Value)));

        Assert.That(field.Values, Is.EqualTo(report.Field),
            "the recovered run must reproduce the in-memory algorithm bitwise — pure tasks make retries invisible");
    }

    #endregion

    #region Tools

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
