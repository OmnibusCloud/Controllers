using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Schwarz;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzDecompose : WitActivityAdapterFunction<WitActivitySchwarzDecompose>
{
    #region Constants

    private const int DEFAULT_PARTS = 4;

    #endregion

    #region Constructors

    public WitActivityAdapterSchwarzDecompose(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzDecompose activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Model, out Guid modelBlobId) || modelBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Model'.");

        if (!pool.TryGetValue(activity.Options, out SchwarzOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get parameter 'Options'.");

        if (options.Coarse)
            throw new InvalidOperationException(
                "The two-level coarse correction is not available in v1 — run with Coarse=false (see the code-reality revision doc).");

        var modelPath = await BlobService.GetLocalPathAsync(modelBlobId);
        var model = SimulationModelDefinition.FromBlobBytes(await File.ReadAllBytesAsync(modelPath));

        var parts = options.Parts > 0 ? options.Parts : DEFAULT_PARTS;
        var definitions = SchwarzDecomposer.Decompose(model, parts, options.Overlap);

        var subdomainBlobIds = new List<Guid>(parts);
        foreach (var definition in definitions)
            subdomainBlobIds.Add(await BlobService.UploadBytesAsync(definition.ToBlobBytes(), $"subdomain_{definition.Index}.owsm"));

        var plan = new SchwarzPlanData
        {
            Parts = parts,
            Dof = definitions.Max(definition => definition.ExtendedNodeCount()),
            Overlap = options.Overlap,
            Eps = options.Eps,
            SubdomainBlobIds = subdomainBlobIds,
            Neighbors = [.. definitions.Select(definition => new SchwarzNeighborsData
            {
                SubdomainIndex = definition.Index,
                Producers = [.. definition.Incoming.Select(map => map.NeighborIndex)]
            })]
        };

        if (!pool.TrySetValue(activity.ReturnReference, plan))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzDecompose CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference model)
                throw new ArgumentException("Parameter 'Model' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivitySchwarzDecompose
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
