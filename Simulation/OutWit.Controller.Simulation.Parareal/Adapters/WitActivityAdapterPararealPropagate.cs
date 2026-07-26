using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Numerics;
using OutWit.Math.Simulation.Parareal;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math

internal sealed class WitActivityAdapterPararealPropagate : WitActivityAdapterFunction<WitActivityPararealPropagate>
{
    #region Constants

    /// <summary>
    /// Benchmark reference size (40³ — see PararealBenchmark), the unit
    /// EstimateWork is expressed in so Work/Rate approximates seconds.
    /// </summary>
    private const int REFERENCE_DOF = 64000;

    private const int MAX_CACHED_STEPPERS = 16;

    #endregion

    #region Fields

    /// <summary>
    /// Per-process fine-stepper cache: adapters are singletons, so the
    /// factorization of M + θδt·A persists across slabs and rounds; keyed by
    /// (model blob, δt). LRU-bounded (factorizations are large) and
    /// poison-evicting (a transient build fault must not stick to the key).
    /// </summary>
    private static readonly SimulationComputeCache<string, FdTransientStepper> m_steppers = new(MAX_CACHED_STEPPERS);

    #endregion

    #region Constructors

    public WitActivityAdapterPararealPropagate(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealPropagate activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out PararealTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        var stepper = await GetOrCreateStepperAsync(task.ModelBlobId, task.TimeStep);

        var statePath = await BlobService.GetLocalPathAsync(task.StateInBlobId);
        var stateIn = PararealStateSnapshot.FromBlobBytes(await File.ReadAllBytesAsync(statePath));

        double[] stateOut;
        Guid? snapshotPackBlobId = null;

        if (task.EmitSnapshots)
        {
            var (final, fractions, snapshots) = PararealFinePropagation.PropagateWithSnapshots(
                stepper, stateIn.Values, task.Steps, task.SnapshotsPerSlab, task.SlabStart);

            var pack = new PararealSnapshotPack
            {
                SlabIndex = task.SlabIndex,
                GlobalDims = (int[])stepper.Problem.Geometry.Dims.Clone()
            };
            for (var i = 0; i < snapshots.Length; i++)
            {
                pack.Times.Add(task.SlabStart + fractions[i] * (task.SlabEnd - task.SlabStart));
                pack.Fields.Add(snapshots[i]);
            }

            snapshotPackBlobId = await BlobService.UploadBytesAsync(pack.ToBlobBytes(), $"snapshots_s{task.SlabIndex}.owsm");
            stateOut = final;
        }
        else
        {
            stateOut = PararealFinePropagation.Propagate(stepper, stateIn.Values, task.Steps, task.SlabStart);
        }

        var outSnapshot = new PararealStateSnapshot
        {
            SlabIndex = task.SlabIndex,
            Round = stateIn.Round,
            Time = task.SlabEnd,
            GlobalDims = (int[])stepper.Problem.Geometry.Dims.Clone(),
            Values = stateOut
        };
        var stateOutBlobId = await BlobService.UploadBytesAsync(outSnapshot.ToBlobBytes(), $"wave_s{task.SlabIndex}.owsm");

        var result = new PararealResultData
        {
            SlabIndex = task.SlabIndex,
            StateOutBlobId = stateOutBlobId,
            SnapshotPackBlobId = snapshotPackBlobId
        };

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    protected override double EstimateWork(WitActivityPararealPropagate activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out PararealTaskData? task) || task == null)
            return 1.0;

        // Work(slab) = steps × (dof/ref)^(4/3) — read from explicit DTO scalars.
        return task.Steps * Math.Pow(task.Dof / (double)REFERENCE_DOF, 4.0 / 3.0);
    }

    public override Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var result = PararealBenchmark.Measure(PararealBenchmark.REFERENCE_SIZE, PararealBenchmark.STEP_COUNT);

        Logger.LogInformation(
            "Parareal.Propagate benchmark: {Rate:F2} {Unit} ({Iterations} steps in {Elapsed})",
            result.Rate, result.Unit, result.Iterations, result.Elapsed);

        return Task.FromResult<IWitBenchmarkResult>(result);
    }

    private async Task<FdTransientStepper> GetOrCreateStepperAsync(Guid modelBlobId, double timeStep)
    {
        var key = $"{modelBlobId:N}:{timeStep:R}";
        var modelPath = await BlobService.GetLocalPathAsync(modelBlobId);

        return m_steppers.GetOrCreate(key, () =>
        {
            var model = SimulationModelDefinition.FromBlobBytes(File.ReadAllBytes(modelPath));

            // Transient: no solvability requirement (the time derivative
            // regularizes the operator) — mirrors the kernel's own build.
            var problem = FdOperatorAssembler.BuildProblem(model, requireSolvability: false);
            var sourceFactor = model.SourceCurve.Count > 0 ? model.SourceFactorAt : (Func<double, double>?)null;
            return new FdTransientStepper(problem, timeStep, theta: 0.5, sourceFactor);
        });
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealPropagate CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivityPararealPropagate
            {
                Task = task
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
