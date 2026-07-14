using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Schwarz;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Controller.Simulation.Schwarz.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzSolveSubdomain : WitActivityAdapterFunction<WitActivitySchwarzSolveSubdomain>
{
    #region Constants

    /// <summary>
    /// Benchmark reference size (40³ — see SchwarzBenchmark), the unit
    /// EstimateWork is expressed in so Work/Rate approximates seconds.
    /// </summary>
    private const int REFERENCE_DOF = 64000;

    private const int MAX_CACHED_SYSTEMS = 16;

    #endregion

    #region Fields

    /// <summary>
    /// Per-process factorization cache: adapters are singletons, so this
    /// persists across tasks and rounds; keyed by the immutable Subdomain blob id
    /// (the BlenderRunner memo discipline). LRU-bounded (factorizations are large)
    /// and poison-evicting (a transient build fault must not stick to the key).
    /// </summary>
    private static readonly SimulationComputeCache<Guid, SchwarzLocalSystem> s_systems = new(MAX_CACHED_SYSTEMS);

    #endregion

    #region Constructors

    public WitActivityAdapterSchwarzSolveSubdomain(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzSolveSubdomain activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out SchwarzTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        var subdomainPath = await BlobService.GetLocalPathAsync(task.SubdomainBlobId);
        var system = GetOrCreateSystem(task.SubdomainBlobId, subdomainPath);

        var strips = new Dictionary<int, double[]>();
        foreach (var boundaryBlobId in task.BoundaryInBlobIds)
        {
            var packagePath = await BlobService.GetLocalPathAsync(boundaryBlobId);
            var package = SchwarzBoundaryPackage.FromBlobBytes(await File.ReadAllBytesAsync(packagePath));
            strips[package.ProducerIndex] = package.GetSlice(task.SubdomainIndex);
        }

        var band = task.BoundaryInBlobIds.Count == 0
            ? system.BuildZeroBand()
            : system.BuildBand(strips, coarseShifts: null);

        var field = system.SolveExtendedField(band);
        var (residualSquared, residualSum) = system.EvaluateLaggedResidual(field, band);

        var outgoing = new SchwarzBoundaryPackage
        {
            ProducerIndex = task.SubdomainIndex,
            Round = task.Round
        };
        foreach (var map in system.Definition.Outgoing)
            outgoing.Slices.Add(new SchwarzBoundarySlice(map.NeighborIndex, system.ExtractOutgoing(map, field)));

        var boundaryOutBlobId = await BlobService.UploadBytesAsync(
            outgoing.ToBlobBytes(),
            $"boundary_s{task.SubdomainIndex}_r{task.Round}.owsm");

        Guid? fieldBlobId = null;
        if (task.EmitField)
        {
            var slice = new SimulationFieldSlice
            {
                PartIndex = task.SubdomainIndex,
                GlobalDims = (int[])system.Definition.GlobalDims.Clone(),
                Lo = (int[])system.Definition.OwnedLo.Clone(),
                Hi = (int[])system.Definition.OwnedHi.Clone(),
                Values = system.ExtractOwned(field)
            };

            fieldBlobId = await BlobService.UploadBytesAsync(slice.ToBlobBytes(), $"field_s{task.SubdomainIndex}.owsm");
        }

        var result = new SchwarzResultData
        {
            SubdomainIndex = task.SubdomainIndex,
            Round = task.Round,
            BoundaryOutBlobId = boundaryOutBlobId,
            ResidualSquared = residualSquared,
            ResidualSum = residualSum,
            FieldBlobId = fieldBlobId
        };

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    protected override double EstimateWork(WitActivitySchwarzSolveSubdomain activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out SchwarzTaskData? task) || task == null)
            return 1.0;

        return Math.Pow(task.Dof / (double)REFERENCE_DOF, 4.0 / 3.0);
    }

    public override Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var result = SchwarzBenchmark.Measure(SchwarzBenchmark.REFERENCE_SIZE, SchwarzBenchmark.SOLVE_COUNT);

        Logger.LogInformation(
            "Schwarz.SolveSubdomain benchmark: {Rate:F2} {Unit} ({Iterations} solves in {Elapsed})",
            result.Rate, result.Unit, result.Iterations, result.Elapsed);

        return Task.FromResult<IWitBenchmarkResult>(result);
    }

    private static SchwarzLocalSystem GetOrCreateSystem(Guid blobId, string path)
    {
        return s_systems.GetOrCreate(blobId, () =>
        {
            var definition = SchwarzSubdomainDefinition.FromBlobBytes(File.ReadAllBytes(path));
            return new SchwarzLocalSystem(definition);
        });
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzSolveSubdomain CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivitySchwarzSolveSubdomain
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
