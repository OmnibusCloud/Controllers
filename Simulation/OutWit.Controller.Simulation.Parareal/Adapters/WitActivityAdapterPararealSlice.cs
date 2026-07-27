using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Parareal;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Parareal;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealSlice : WitActivityAdapterFunction<WitActivityPararealSlice>
{
    #region Constants

    private const int DEFAULT_SLABS = 4;

    #endregion

    #region Constructors

    public WitActivityAdapterPararealSlice(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealSlice activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Model, out Guid modelBlobId) || modelBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Model'.");

        if (!pool.TryGetValue(activity.Options, out PararealOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get parameter 'Options'.");

        if (options.SnapshotsPerSlab < 1 || options.SnapshotsPerSlab > options.FineStepsPerSlab)
            throw new InvalidOperationException(
                $"SnapshotsPerSlab must lie in [1, FineStepsPerSlab={options.FineStepsPerSlab}], got {options.SnapshotsPerSlab}.");

        var slabs = options.Slabs > 0 ? options.Slabs : DEFAULT_SLABS;

        // Building the kernel validates the model, the time parameters and the
        // coarsening divisibility, and warms the cache Init/Correct reuse.
        var kernel = await PararealKernelCache.GetOrCreateAsync(
            BlobService, modelBlobId, slabs, options.Coarsening, options.TotalTime, options.FineStepsPerSlab);

        // Closes the silent never-converge hole and bounds the timeline blob
        // BEFORE any propagation is scheduled.
        PararealOptionsValidator.Validate(options, slabs, kernel.FineProblem.Geometry.NodeCount);

        // The last boundary is pinned to TotalTime exactly (TotalTime·N/N may
        // round differently) — task δt and the kernel cache key derive from it.
        var boundaries = Enumerable.Range(0, slabs + 1).Select(i => options.TotalTime * i / slabs).ToList();
        boundaries[^1] = options.TotalTime;

        var plan = new PararealPlanData
        {
            Slabs = slabs,
            Dof = kernel.FineProblem.Geometry.NodeCount,
            SlabBoundaries = boundaries,
            ModelBlobId = modelBlobId,
            FineStepsPerSlab = options.FineStepsPerSlab,
            Coarsening = kernel.Coarsening,
            Eps = options.Eps,
            SnapshotsPerSlab = options.SnapshotsPerSlab
        };

        if (!pool.TrySetValue(activity.ReturnReference, plan))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealSlice CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference model)
                throw new ArgumentException("Parameter 'Model' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityPararealSlice
            {
                Model = model,
                Options = options
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
