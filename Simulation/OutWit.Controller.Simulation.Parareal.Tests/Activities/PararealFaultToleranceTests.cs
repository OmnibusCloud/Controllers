using Microsoft.Extensions.DependencyInjection;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Parareal;
using OutWit.Controller.Simulation.Parareal.Tests.Mock;
using OutWit.Controller.Simulation.Parareal.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Simulation.Parareal.Tests.Activities;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math

/// <summary>
/// Deep-dive §10 fault injection: a vanished contributor mid-wave costs one
/// re-download + re-run of one slab; the job completes and the result is
/// bitwise-identical to the healthy run.
/// </summary>
[TestFixture]
public class PararealFaultToleranceTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private SimulationTestBlobService m_blobService = null!;
    private string m_pararealScript = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SimulationTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var scriptPath = SimulationTestPaths.GetPararealSolveScriptPath(solutionRoot);
        if (!File.Exists(scriptPath))
            Assert.Ignore($"PararealSolve.wit not found at {scriptPath}");

        m_pararealScript = File.ReadAllText(scriptPath);

        var controllersPath = SimulationTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_parareal_faulttest_{Guid.NewGuid():N}");
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
        var n = 17;
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition { Nx = n, Ny = n, Hx = h, Hy = h };
        var initial = new double[model.NodeCount];
        for (var node = 0; node < initial.Length; node++)
        {
            var x = node % n * h;
            var y = node / n * h;
            initial[node] = Math.Sin(Math.PI * x) * Math.Sin(Math.PI * y);
        }
        model.InitialPerNode = initial;
        foreach (var face in new[] { SimulationFace.XMin, SimulationFace.XMax, SimulationFace.YMin, SimulationFace.YMax })
            model.Boundaries.Add(new SimulationBoundaryCondition(face, SimulationBcKind.Dirichlet, 0));

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 4,
            Eps = 1e-6,
            MaxIterations = 10,
            Coarsening = 0,
            TotalTime = 0.5,
            FineStepsPerSlab = 5
        };

        var job = m_engine.Compile(m_pararealScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed),
            "a single node failure must be absorbed by reassignment, not fail the job");

        var report = PararealInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var state = job.Variables["state"].Value as PararealStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(report.Iterations));

        for (var slab = 0; slab < state.StateBlobIds.Count; slab++)
        {
            var path = m_blobService.GetStoredPath(state.StateBlobIds[slab]);
            var snapshot = PararealStateSnapshot.FromBlobBytes(await File.ReadAllBytesAsync(path));
            Assert.That(snapshot.Values, Is.EqualTo(report.SlabStates[slab]),
                $"slab {slab}: the recovered run must reproduce the in-memory algorithm bitwise");
        }
    }

    #endregion
}
