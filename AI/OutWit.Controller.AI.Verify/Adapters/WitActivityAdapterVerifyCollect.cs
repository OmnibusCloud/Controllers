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

internal sealed class WitActivityAdapterVerifyCollect : WitActivityAdapterFunction<WitActivityVerifyCollect>
{
    #region Constructors

    public WitActivityAdapterVerifyCollect(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyCollect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Results, out var resultsObject) || resultsObject is not IEnumerable resultsEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Results'.");

        // Grid.ForEach returns batches in completion order; results carry their own TaskIndex,
        // so flatten and let the report writer re-key.
        var results = resultsEnumerable
            .OfType<VerifyResultBatchData>()
            .SelectMany(batch => batch.Results)
            .ToList();

        var reportBytes = VerifyReportWriter.Write(results);
        var reportBlobId = await BlobService.UploadBytesAsync(reportBytes, "verify-report.json");

        if (!pool.TrySetValue(activity.ReturnReference, reportBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyCollect CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference results)
                throw new ArgumentException("Parameter 'Results' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityVerifyCollect
            {
                Results = results,
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
