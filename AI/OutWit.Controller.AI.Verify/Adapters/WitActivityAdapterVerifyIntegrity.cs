using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyIntegrity : WitActivityAdapterFunction<WitActivityVerifyIntegrity>
{
    #region Constants

    private const string BLOB_NAME = "verify-integrity.json";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyIntegrity(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyIntegrity activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Results, out var resultsObject) || resultsObject is not IEnumerable resultsEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Results'.");

        if (!pool.TryGetObject(activity.Replicas, out var replicasObject) || replicasObject is not IEnumerable replicasEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Replicas'.");

        var primaries = resultsEnumerable.OfType<VerifyResultBatchData>().ToList();
        var replicas = replicasEnumerable.OfType<VerifyResultBatchData>().ToList();

        var bytes = VerifyIntegrityWriter.Write(primaries, replicas);
        var blobId = await BlobService.UploadBytesAsync(bytes, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyIntegrity CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference results)
                throw new ArgumentException("Parameter 'Results' must be a variable reference.");

            if (parameters[1] is not IWitReference replicas)
                throw new ArgumentException("Parameter 'Replicas' must be a variable reference.");

            return new WitActivityVerifyIntegrity
            {
                Results = results,
                Replicas = replicas
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
