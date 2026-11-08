using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusCollect : WitActivityAdapterFunction<WitActivityCorpusCollect>
{
    #region Constants

    private const string BLOB_NAME = "corpus-report.json";

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusCollect(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusCollect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Results, out var resultsObject) || resultsObject is not IEnumerable resultsEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Results'.");

        // Grid.ForEach returns results in completion order; each carries its TaskIndex and the
        // report writer re-keys.
        var results = resultsEnumerable.OfType<CorpusResultData>().ToList();

        var reportBytes = CorpusReportWriter.Write(results);
        var reportBlobId = await BlobService.UploadBytesAsync(reportBytes, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, reportBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusCollect CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference results)
                throw new ArgumentException("Parameter 'Results' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityCorpusCollect
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
