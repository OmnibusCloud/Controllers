using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.MemoryPack;
using OutWit.Common.NUnit;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
using OutWit.Controller.Simulation.Model.Schwarz;
using OutWit.Controller.Simulation.Schwarz.Tests.Mock;
using OutWit.Controller.Simulation.Schwarz.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Activities;

/// <summary>
/// Distributed gates: the bundled SchwarzSolve.wit script running through
/// the engine (Loop{Grid.ForEach}+Break, blob transport, mock nodes) must
/// reproduce the in-memory algorithm reference exactly, and both must match the
/// single-machine reference solver.
/// </summary>
[TestFixture]
public class SchwarzSolveSkeletonTests
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

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_simulation_blobtest_{Guid.NewGuid():N}");
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
        var job = m_engine.Compile(m_schwarzScript);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));

        // A silently-wrong compile (missing statements) must not pass as "compiles".
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "model", "opts", "plan", "state", "maxRounds", "tasks", "wave", "converged", "field" }));

        // NB: no MemoryPackClone on the whole job — a job containing a
        // Grid.ForEach transform is not wire-serializable by design (only the
        // transformer activity crosses to nodes; scripts travel as text).
    }

    [Test]
    public async Task ConvergedDistributedRunMatchesInMemoryTest()
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

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var state = job.Variables["state"].Value as SchwarzRoundData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(report.Rounds), "distributed and in-memory round counts must match");
        Assert.That(job.Variables["converged"].Value, Is.EqualTo(true));

        var field = await DownloadFieldAsync(job);
        Assert.That(field.Values, Is.EqualTo(report.Field),
            "distributed field must reproduce the in-memory algorithm exactly");

        // And both must solve the actual problem.
        var reference = SimulationReferenceSolver.Solve(model);
        var scale = FixedOrderNorms.MaxAbs(reference);
        var difference = FixedOrderNorms.MaxAbsDifference(field.Values, reference);
        TestContext.Out.WriteLine($"distributed rounds: {state.Round}, relative parity vs reference: {difference / scale:E3}");
        Assert.That(difference / scale, Is.LessThanOrEqualTo(1e-9));
    }

    [Test]
    public async Task BudgetExhaustedRunCompletesUnconvergedTest()
    {
        var model = CreateMms2dModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new SchwarzOptionsData
        {
            Parts = 2,
            Overlap = 2,
            Eps = 1e-30,
            MaxRounds = 3
        };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var state = job.Variables["state"].Value as SchwarzRoundData;
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.Round, Is.EqualTo(3));
        Assert.That(job.Variables["converged"].Value, Is.EqualTo(false));

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.False);

        var field = await DownloadFieldAsync(job);
        Assert.That(field.Values, Is.EqualTo(report.Field));
    }

    [Test]
    public async Task CoarseRequestFailsLoudTest()
    {
        var model = CreateMms2dModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var options = new SchwarzOptionsData
        {
            Parts = 2,
            Overlap = 2,
            Eps = 1e-8,
            MaxRounds = 10,
            Coarse = true
        };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed),
            "a v1 job requesting the coarse correction must fail loudly, not run degraded");
    }

    [Test]
    public async Task ConvergedDistributed3dRunMatchesInMemoryTest()
    {
        // 3D geometry through the REAL pipeline: 3D subdomain blobs, 3D strips,
        // 3D field slices — the in-memory 3D parity alone never exercises them.
        var n = 11;
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition
        {
            Nx = n, Ny = n, Nz = n, Hx = h, Hy = h, Hz = h,
            SourceConstant = 1
        };
        foreach (var face in Enum.GetValues<SimulationFace>())
            model.Boundaries.Add(new SimulationBoundaryCondition(face, SimulationBcKind.Dirichlet, 0));

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model3d.owsm");
        var options = new SchwarzOptionsData { Parts = 4, Overlap = 2, Eps = 1e-8, MaxRounds = 500 };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var field = await DownloadFieldAsync(job);
        Assert.That(field.GlobalDims, Is.EqualTo(new[] { n, n, n }));
        Assert.That(field.Values, Is.EqualTo(report.Field), "3D distributed field must match in-memory bitwise");
    }

    [Test]
    public async Task ConvergedDistributedRobinRunMatchesInMemoryTest()
    {
        // Robin gate: Robin faces through the REAL pipeline — the model blob
        // carries the constant-pair BC encoding, the subdomain blobs carry the
        // per-face film coefficients, and the distributed field must still be
        // bitwise-identical to the in-memory algorithm.
        var n = 25;
        var h = 1.0 / (n - 1);
        var model = new SimulationModelDefinition
        {
            Nx = n,
            Ny = n,
            Hx = h,
            Hy = h,
            ConductivityConstant = 2
        };
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMin, SimulationBcKind.Dirichlet, 1));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMax, SimulationBcKind.Robin, 10, 5));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMin, SimulationBcKind.Neumann, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMax, SimulationBcKind.Neumann, 0));

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model-robin.owsm");
        var options = new SchwarzOptionsData { Parts = 4, Overlap = 3, Eps = 1e-10, MaxRounds = 500 };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);
        Assert.That(job.Variables["converged"].Value, Is.EqualTo(true));

        var field = await DownloadFieldAsync(job);
        Assert.That(field.Values, Is.EqualTo(report.Field), "Robin distributed field must match in-memory bitwise");

        // And the solved wall must obey the analytic linear profile u = 1 + 45/7·x.
        var reference = SimulationReferenceSolver.Solve(model);
        var difference = FixedOrderNorms.MaxAbsDifference(field.Values, reference);
        var scale = FixedOrderNorms.MaxAbs(reference);
        TestContext.Out.WriteLine($"Robin distributed relative parity vs reference: {difference / scale:E3}");
        Assert.That(difference / scale, Is.LessThanOrEqualTo(1e-9));
    }

    [Test]
    public async Task ConvergedDistributedMixedBcNonSquareRunTest()
    {
        // Neumann face roles must survive the subdomain-blob round-trip and the
        // full distributed path — every other distributed model is all-Dirichlet.
        var model = new SimulationModelDefinition
        {
            Nx = 21, Ny = 17,
            Hx = 1.0 / 20, Hy = 1.0 / 16,
            ConductivityPerNode = Enumerable.Range(0, 21 * 17).Select(i => 1.0 + 0.002 * (i % 37)).ToArray(),
            SourcePerNode = Enumerable.Range(0, 21 * 17).Select(i => Math.Sin(0.1 * i)).ToArray()
        };
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMin, SimulationBcKind.Dirichlet, 1));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.XMax, SimulationBcKind.Neumann, 0.5));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMin, SimulationBcKind.Neumann, 0));
        model.Boundaries.Add(new SimulationBoundaryCondition(SimulationFace.YMax, SimulationBcKind.Dirichlet, -1));

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model_mixed.owsm");
        var options = new SchwarzOptionsData { Parts = 2, Overlap = 2, Eps = 1e-10, MaxRounds = 500 };

        var job = m_engine.Compile(m_schwarzScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var report = SchwarzInMemorySolver.Solve(model, options);
        Assert.That(report.Converged, Is.True);

        var field = await DownloadFieldAsync(job);
        Assert.That(field.Values, Is.EqualTo(report.Field), "mixed-BC distributed field must match in-memory bitwise");
    }

    [Test]
    public async Task SecondRunOnWarmCachesIsBitwiseIdenticalTest()
    {
        // Long-lived nodes hit the factorization cache on every job after the
        // first — the warm path is the NORMAL production path.
        var model = CreateMms2dModel(17);
        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model_warm.owsm");
        var options = new SchwarzOptionsData { Parts = 3, Overlap = 2, Eps = 1e-8, MaxRounds = 500 };

        var firstJob = m_engine.Compile(m_schwarzScript);
        var firstStatus = await m_engine.ScheduleAndWaitAsync(firstJob, modelBlobId, options);
        Assert.That(firstStatus.Result, Is.EqualTo(WitProcessingResult.Completed));
        var firstField = await DownloadFieldAsync(firstJob);

        var secondJob = m_engine.Compile(m_schwarzScript);
        var secondStatus = await m_engine.ScheduleAndWaitAsync(secondJob, modelBlobId, options);
        Assert.That(secondStatus.Result, Is.EqualTo(WitProcessingResult.Completed));
        var secondField = await DownloadFieldAsync(secondJob);

        Assert.That(secondField.Values, Is.EqualTo(firstField.Values),
            "a warm-cache re-run must be bitwise-identical to the cold run");
    }

    #endregion

    #region Co-Load Tests

    [Test]
    public async Task PararealModuleCoLoadsWithSharedModelTest()
    {
        var script = """
                     Job:PararealProbe(Blob:model, PararealOptions:opts)
                     {
                         PararealPlan:plan = Parareal.Slice(model, opts);
                         Int:maxIter = Parareal.IterationBudget(opts);
                     }
                     """;

        var modelBlobId = await m_blobService.UploadBytesAsync(CreateMms2dModel(9).ToBlobBytes(), "model.owsm");
        var options = new PararealOptionsData
        {
            Slabs = 5,
            MaxIterations = 7
        };

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        // The cast proves type identity across the boundary: the plan produced by
        // the parareal.module's adapter must be the SAME Simulation.Model type the
        // test assembly compiled against, while schwarz.module (same shared DLL)
        // is loaded alongside — the duplicate-Model-assembly question.
        var plan = job.Variables["plan"].Value as PararealPlanData;
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.Slabs, Is.EqualTo(5));
        Assert.That(plan.SlabBoundaries, Has.Count.EqualTo(6));

        Assert.That(job.Variables["maxIter"].Value, Is.EqualTo(7));
    }

    #endregion

    #region Wire Tests

    [Test]
    public void DtoMemoryPackRoundTripTest()
    {
        var task = new SchwarzTaskData
        {
            SubdomainIndex = 2,
            SubdomainBlobId = Guid.NewGuid(),
            Dof = 262144,
            Round = 3,
            BoundaryInBlobIds = [Guid.NewGuid(), Guid.NewGuid()],
            EmitField = true
        };
        Assert.That(task.MemoryPackClone(), Was.EqualTo(task));

        var result = new SchwarzResultData
        {
            SubdomainIndex = 2,
            Round = 3,
            BoundaryOutBlobId = Guid.NewGuid(),
            ResidualSquared = 0.125,
            ResidualSum = -0.5,
            FieldBlobId = Guid.NewGuid()
        };
        Assert.That(result.MemoryPackClone(), Was.EqualTo(result));

        var round = new SchwarzRoundData
        {
            Round = 4,
            Residual = 0.03125,
            InitialResidual = 0.5,
            Eps = 0.1,
            Boundaries =
            [
                new SchwarzBoundarySetData { SubdomainIndex = 0, BlobIds = [Guid.NewGuid()] },
                new SchwarzBoundarySetData { SubdomainIndex = 1, BlobIds = [Guid.NewGuid()] }
            ]
        };
        Assert.That(round.MemoryPackClone(), Was.EqualTo(round));

        var plan = new SchwarzPlanData
        {
            Parts = 2,
            Dof = 262144,
            Overlap = 3,
            Eps = 0.00000001,
            SubdomainBlobIds = [Guid.NewGuid(), Guid.NewGuid()],
            Neighbors =
            [
                new SchwarzNeighborsData { SubdomainIndex = 0, Producers = [1] },
                new SchwarzNeighborsData { SubdomainIndex = 1, Producers = [0] }
            ]
        };
        Assert.That(plan.MemoryPackClone(), Was.EqualTo(plan));

        var schwarzOptions = new SchwarzOptionsData
        {
            Parts = 8,
            Overlap = 4,
            Eps = 0.0001,
            MaxRounds = 30,
            Coarse = false,
            ThreadsPerNode = 2
        };
        Assert.That(schwarzOptions.MemoryPackClone(), Was.EqualTo(schwarzOptions));

        var pararealOptions = new PararealOptionsData
        {
            Slabs = 16,
            Eps = 0.000001,
            MaxIterations = 10,
            Coarsening = 2,
            SliceByBenchmark = false
        };
        Assert.That(pararealOptions.MemoryPackClone(), Was.EqualTo(pararealOptions));

        var pararealPlan = new PararealPlanData
        {
            Slabs = 4,
            Dof = 262144,
            SlabBoundaries = [0, 0.25, 0.5, 0.75, 1]
        };
        Assert.That(pararealPlan.MemoryPackClone(), Was.EqualTo(pararealPlan));
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

    private async Task<SimulationFieldSlice> DownloadFieldAsync(IWitJob job)
    {
        var fieldBlobId = job.Variables["field"].Value as Guid?;
        Assert.That(fieldBlobId, Is.Not.Null);
        Assert.That(fieldBlobId!.Value, Is.Not.EqualTo(Guid.Empty));

        var path = m_blobService.GetStoredPath(fieldBlobId.Value);
        return SimulationFieldSlice.FromBlobBytes(await File.ReadAllBytesAsync(path));
    }

    #endregion
}
