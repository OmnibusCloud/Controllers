using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
using OutWit.Controller.Simulation.Model.Parareal;
using OutWit.Controller.Simulation.Parareal.Tests.Mock;
using OutWit.Controller.Simulation.Parareal.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Simulation.Parareal.Tests.Activities;

[TestFixture]
[Explicit("Distributed-run diagnostics")]
public class PararealDiagnosticsTests
{
    private SimulationTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string m_script = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = SimulationTestPaths.FindSolutionRoot()!;
        m_script = File.ReadAllText(SimulationTestPaths.GetPararealSolveScriptPath(solutionRoot));
        var controllersPath = SimulationTestPaths.FindControllersPath()!;

        m_blobService = new SimulationTestBlobService(Path.Combine(Path.GetTempPath(), $"parareal_diag_{Guid.NewGuid():N}"));

        WitEngineNodeSdk.Instance.Reload(false, moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));
        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(false, null, controllersPath, services =>
        {
            services.AddSingleton<IWitBlobService>(m_blobService);
            services.AddSingleton<IWitNodesManager>(new SimulationTestNodesManager(WitEngineNodeSdk.Instance));
        });
    }

    [Test]
    public void NodePathInIsolationTest()
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

        var kernel = new PararealKernel(model, 4, 0, 0.5, 5);
        var u0 = kernel.BuildInitialField();
        var expected = kernel.FinePropagate(u0);

        // Node path: model through the blob, own stepper with the canonical δt.
        var restored = SimulationModelDefinition.FromBlobBytes(model.ToBlobBytes());
        var problem = FdOperatorAssembler.BuildProblem(restored);
        var stepper = new FdTransientStepper(problem, 0.5 / (4 * 5), theta: 0.5);
        var actual = PararealFinePropagation.Propagate(stepper, u0, 5);

        TestContext.Out.WriteLine($"maxdiff node-vs-kernel F(u0): {FixedOrderNorms.MaxAbsDifference(actual, expected):E3}");
        TestContext.Out.WriteLine($"expected[18]={expected[18]:E6} actual[18]={actual[18]:E6}");
    }

    [Test]
    public async Task OneIterationStateComparisonTest()
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

        var options = new PararealOptionsData
        {
            Slabs = 4, Eps = 1e-300, MaxIterations = 1, Coarsening = 0, TotalTime = 0.5, FineStepsPerSlab = 5
        };

        var modelBlobId = await m_blobService.UploadBytesAsync(model.ToBlobBytes(), "model.owsm");
        var job = m_engine.Compile(m_script);
        var status = await m_engine.ScheduleAndWaitAsync(job, modelBlobId, options);
        TestContext.Out.WriteLine($"status: {status.Result}");

        var report = PararealInMemorySolver.Solve(model, options);
        var state = (PararealStateData)job.Variables["state"].Value!;

        TestContext.Out.WriteLine($"in-memory correction: {report.CorrectionHistory[^1]:E17}");
        TestContext.Out.WriteLine($"distributed correction: {state.CorrectionNorm:E17}");

        for (var slab = 0; slab <= options.Slabs; slab++)
        {
            var path = m_blobService.GetStoredPath(state.StateBlobIds[slab]);
            var snapshot = PararealStateSnapshot.FromBlobBytes(await File.ReadAllBytesAsync(path));
            var diff = FixedOrderNorms.MaxAbsDifference(snapshot.Values, report.SlabStates[slab]);
            TestContext.Out.WriteLine($"slab {slab}: maxdiff={diff:E3}  dist[18]={snapshot.Values[18]:E6}  mem[18]={report.SlabStates[slab][18]:E6}  round={snapshot.Round}");
        }
    }
}
