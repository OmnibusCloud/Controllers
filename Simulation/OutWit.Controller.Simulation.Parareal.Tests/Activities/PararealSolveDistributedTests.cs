using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Numerics;
using OutWit.Math.Simulation.Parareal;
using OutWit.Controller.Simulation.Parareal.Tests.Mock;
using OutWit.Controller.Simulation.Parareal.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Simulation.Parareal.Tests.Activities;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math
using OutWit.Math.Simulation.Model.Parareal;
using OutWit.Math.Simulation.Model.Problem;

/// <summary>
/// Distributed gates: the bundled PararealSolve.wit script running through
/// the engine (Loop{Grid.ForEach}+Break, blob transport, mock nodes) must
/// reproduce the in-memory algorithm reference exactly.
/// </summary>
[TestFixture]
public class PararealSolveDistributedTests
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

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_parareal_blobtest_{Guid.NewGuid():N}");
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
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Script Tests

    [Test]
    public void BundledScriptCompilesTest()
    {
        var job = m_engine.Compile(m_pararealScript);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));

        // A silently-wrong compile (missing statements) must not pass as "compiles".
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "model", "opts", "plan", "state", "maxIter", "tasks", "wave", "converged", "timeline" }));
    }

    [Test]
    public async Task ConvergedDistributedRunMatchesInMemoryTest()
    {
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

        var job = m_engine.Compile(m_pararealScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = PararealInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var state = job.Variables["state"].Value as PararealStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(report.Iterations), "distributed and in-memory iteration counts must match");
        Assert.That(state.CorrectionNorm, Is.EqualTo(report.CorrectionHistory[^1]), "correction norms must match bitwise");
        Assert.That(job.Variables["converged"].Value, Is.EqualTo(true));

        // Slab-boundary states must reproduce the in-memory algorithm exactly.
        for (var slab = 0; slab < state.StateBlobIds.Count; slab++)
        {
            var snapshot = await DownloadStateAsync(state.StateBlobIds[slab]);
            Assert.That(snapshot.Values, Is.EqualTo(report.SlabStates[slab]), $"slab {slab} state must match bitwise");
        }

        // Timeline: one snapshot per slab (slab-end recomputes from converged states).
        var timeline = await DownloadTimelineAsync(job);
        Assert.That(timeline.Times, Has.Count.EqualTo(options.Slabs));
        Assert.That(timeline.Times, Is.EqualTo(Enumerable.Range(1, options.Slabs).Select(i => options.TotalTime * i / options.Slabs)).Within(1e-12));

        var kernel = new PararealKernel(model, options.Slabs, options.Coarsening, options.TotalTime, options.FineStepsPerSlab);
        for (var slab = 0; slab < options.Slabs; slab++)
        {
            var expected = kernel.FinePropagate(report.SlabStates[slab]);
            Assert.That(timeline.Fields[slab], Is.EqualTo(expected), $"timeline snapshot {slab} must equal F(U_{slab}) bitwise");
        }

        // And the timeline's final state solves the actual problem.
        var serial = PararealInMemorySolver.SolveSerialFine(model, options);
        var scale = serial.Max(fields => FixedOrderNorms.MaxAbs(fields));
        var difference = FixedOrderNorms.MaxAbsDifference(timeline.Fields[^1], serial[^1]);
        TestContext.Out.WriteLine($"iterations: {state.Round}, final deviation from serial fine: {difference / scale:E3} of problem scale");
        Assert.That(difference / scale, Is.LessThanOrEqualTo(1e-5));
    }

    [Test]
    public async Task BudgetExhaustedRunCompletesUnconvergedTest()
    {
        var model = CreateHeatModel(9);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 3,
            Eps = 1e-300,
            MaxIterations = 2,
            Coarsening = 1,
            TotalTime = 0.2,
            FineStepsPerSlab = 4
        };

        var job = m_engine.Compile(m_pararealScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = PararealInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.False);

        var state = job.Variables["state"].Value as PararealStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(2));
        Assert.That(job.Variables["converged"].Value, Is.EqualTo(false));

        for (var slab = 0; slab < state.StateBlobIds.Count; slab++)
        {
            var snapshot = await DownloadStateAsync(state.StateBlobIds[slab]);
            Assert.That(snapshot.Values, Is.EqualTo(report.SlabStates[slab]), $"slab {slab} state must match bitwise");
        }
    }

    [Test]
    public async Task InteriorSnapshotsTimelineTest()
    {
        // SnapshotsPerSlab > 1: every other test uses 1, so the
        // interior-snapshot schedule would otherwise ship untested.
        var model = CreateHeatModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model_snap.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 3,
            Eps = 1e-6,
            MaxIterations = 10,
            Coarsening = 0,
            TotalTime = 0.3,
            FineStepsPerSlab = 4,
            SnapshotsPerSlab = 2
        };

        var job = m_engine.Compile(m_pararealScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = PararealInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var timeline = await DownloadTimelineAsync(job);
        Assert.That(timeline.Times, Has.Count.EqualTo(options.Slabs * options.SnapshotsPerSlab));
        Assert.That(timeline.Times, Is.Ordered.Ascending);
        Assert.That(timeline.Times[^1], Is.EqualTo(options.TotalTime).Within(1e-12));

        // Each slab's LAST snapshot is the slab-end recompute F(U_n) — bitwise.
        var kernel = new PararealKernel(model, options.Slabs, options.Coarsening, options.TotalTime, options.FineStepsPerSlab);
        for (var slab = 0; slab < options.Slabs; slab++)
        {
            var expected = kernel.FinePropagate(report.SlabStates[slab]);
            var slabEndIndex = slab * options.SnapshotsPerSlab + options.SnapshotsPerSlab - 1;
            Assert.That(timeline.Fields[slabEndIndex], Is.EqualTo(expected),
                $"slab {slab} end snapshot must equal F(U_{slab}) bitwise");
        }
    }

    [Test]
    public async Task ConvergedDistributed3dRunMatchesInMemoryTest()
    {
        // 3D transient through the real pipeline with spatial coarsening 2.
        var n = 9;
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition { Nx = n, Ny = n, Nz = n, Hx = h, Hy = h, Hz = h };
        var initial = new double[model.NodeCount];
        for (var node = 0; node < initial.Length; node++)
        {
            var x = node % n * h;
            var y = node / n % n * h;
            var z = node / (n * n) * h;
            initial[node] = Math.Sin(Math.PI * x) * Math.Sin(Math.PI * y) * Math.Sin(Math.PI * z);
        }
        model.InitialPerNode = initial;
        foreach (var face in Enum.GetValues<SimulationFace>())
            model.Boundaries.Add(new SimulationBoundaryCondition(face, SimulationBcKind.Dirichlet, 0));

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model3d.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 3,
            Eps = 1e-6,
            MaxIterations = 10,
            Coarsening = 2,
            TotalTime = 0.3,
            FineStepsPerSlab = 4
        };

        var job = m_engine.Compile(m_pararealScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = PararealInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var state = job.Variables["state"].Value as PararealStateData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(report.Iterations));

        for (var slab = 0; slab < state.StateBlobIds.Count; slab++)
        {
            var snapshot = await DownloadStateAsync(state.StateBlobIds[slab]);
            Assert.That(snapshot.Values, Is.EqualTo(report.SlabStates[slab]), $"3D slab {slab} state must match bitwise");
        }
    }

    [Test]
    public async Task SecondRunOnWarmCachesIsBitwiseIdenticalTest()
    {
        var model = CreateHeatModel(9);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model_warm.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 3,
            Eps = 1e-6,
            MaxIterations = 10,
            Coarsening = 1,
            TotalTime = 0.3,
            FineStepsPerSlab = 4
        };

        var firstJob = m_engine.Compile(m_pararealScript);
        Assert.That((await m_engine.ScheduleAndWaitAsync(firstJob, modelBlobId, options)).Result, Is.EqualTo(WitProcessingResult.Completed));
        var firstTimeline = await DownloadTimelineAsync(firstJob);

        var secondJob = m_engine.Compile(m_pararealScript);
        Assert.That((await m_engine.ScheduleAndWaitAsync(secondJob, modelBlobId, options)).Result, Is.EqualTo(WitProcessingResult.Completed));
        var secondTimeline = await DownloadTimelineAsync(secondJob);

        Assert.That(secondTimeline.Times, Is.EqualTo(firstTimeline.Times));
        for (var i = 0; i < firstTimeline.Fields.Count; i++)
            Assert.That(secondTimeline.Fields[i], Is.EqualTo(firstTimeline.Fields[i]), $"snapshot {i} must be bitwise-identical on the warm re-run");
    }

    #endregion

    #region Wire Tests

    [Test]
    public void DtoMemoryPackRoundTripTest()
    {
        var state = new PararealStateData
        {
            Round = 3,
            CorrectionNorm = 0.001,
            Scale = 1.5,
            Eps = 0.000001,
            StateBlobIds = [Guid.NewGuid(), Guid.NewGuid()]
        };
        Assert.That(state.MemoryPackClone(), Was.EqualTo(state));

        var task = new PararealTaskData
        {
            SlabIndex = 2,
            ModelBlobId = Guid.NewGuid(),
            StateInBlobId = Guid.NewGuid(),
            SlabStart = 0.25,
            SlabEnd = 0.5,
            Steps = 10,
            Dof = 4913,
            EmitSnapshots = true,
            SnapshotsPerSlab = 2
        };
        Assert.That(task.MemoryPackClone(), Was.EqualTo(task));

        var result = new PararealResultData
        {
            SlabIndex = 2,
            StateOutBlobId = Guid.NewGuid(),
            SnapshotPackBlobId = Guid.NewGuid()
        };
        Assert.That(result.MemoryPackClone(), Was.EqualTo(result));

        var plan = new PararealPlanData
        {
            Slabs = 4,
            Dof = 4913,
            SlabBoundaries = [0, 0.125, 0.25, 0.375, 0.5],
            ModelBlobId = Guid.NewGuid(),
            FineStepsPerSlab = 5,
            Coarsening = 2,
            Eps = 0.000001,
            SnapshotsPerSlab = 1
        };
        Assert.That(plan.MemoryPackClone(), Was.EqualTo(plan));
    }

    #endregion

    #region Tools

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

    private async Task<PararealStateSnapshot> DownloadStateAsync(Guid blobId)
    {
        var path = m_blobService.GetStoredPath(blobId);
        return PararealStateSnapshot.FromBlobBytes(await File.ReadAllBytesAsync(path));
    }

    private async Task<PararealTimeline> DownloadTimelineAsync(IWitJob job)
    {
        var timelineBlobId = job.Variables["timeline"].Value as Guid?;
        Assert.That(timelineBlobId, Is.Not.Null);
        Assert.That(timelineBlobId!.Value, Is.Not.EqualTo(Guid.Empty));

        var path = m_blobService.GetStoredPath(timelineBlobId.Value);
        return PararealTimeline.FromBlobBytes(await File.ReadAllBytesAsync(path));
    }

    #endregion
}
