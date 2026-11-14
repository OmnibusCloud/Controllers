using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusPreflight : WitActivityAdapterFunction<WitActivityCorpusPreflight>
{
    #region Constants

    private static readonly Lazy<HttpClient> CLIENT = new(() => ShardHttp.CreateClient(ShardOriginPolicy.Machine));

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusPreflight(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusPreflight activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var (plan, manifest, options, inputBytes) = await CorpusPlanning.PlanAsync(BlobService, pool, activity.Manifest, activity.LanguageModel, activity.Options, "Manifest");
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        var report = new CorpusPreflightData
        {
            WellFormed = plan.Errors.Count == 0 && plan.Tasks.Count > 0,
            TaskCount = plan.Tasks.Count,
            RefusedOrigins = [.. plan.RefusedOrigins],
            OversizedSources = plan.OversizedSources,
            EstimatedTrafficBytes = plan.EstimatedTrafficBytes,
            Messages = [.. plan.Errors, .. plan.Notes]
        };

        // Sources without a size: ask the origin (HEAD) so the traffic line is not 0, within a
        // probe budget; the rest is extrapolated at the mean and the message says so.
        var sizes = manifest.Sources.Select(source => source.SizeBytes).ToList();
        if (plan.Errors.Count == 0 && manifest.Sources.Any(source => source.SizeBytes <= 0))
        {
            var origins = manifest.AllowedOrigins.Select(ShardOrigin.Normalize).ToList();
            var probe = await ShardSizeProbe.ProbeAsync(manifest.Sources, origins, ShardOriginPolicy.Machine, CLIENT.Value, cancellationToken);
            report.EstimatedTrafficBytes += probe.EstimatedBytes;
            report.Messages.Add($"{probe.Probed} source size(s) probed at the origin" +
                                (probe.Failed > 0 ? $", {probe.Failed} unanswered" : "") +
                                (probe.Unprobed > 0 ? $", {probe.Unprobed} not asked (beyond the probe budget, or a host this server does not fetch from)" : "") +
                                (probe.Failed + probe.Unprobed > 0 ? " (estimated at the mean)" : "") +
                                $"; traffic estimate {report.EstimatedTrafficBytes} bytes.");
            if (probe.Sizes.Count == sizes.Count)
                sizes = [.. probe.Sizes];
        }

        // What the job leaves on the server, from the same sizes, against the deployment's headroom.
        if (manifest.Sources.Count > 0)
        {
            var sized = manifest.Sources.Select((source, index) => new CorpusSourceData { Url = source.Url, Format = source.Format, SizeBytes = sizes[index] }).ToList();
            var (estimate, limit, refusal) = await CorpusPlanning.CheckStorageAsync(sized, manifest.Pipeline, options, inputBytes, new CorpusStorageHeadroomHost(BlobService), cancellationToken);

            report.EstimatedStorageBytes = estimate.TotalBytes;
            report.StorageHeadroomBytes = limit.HeadroomBytes;
            report.StorageRetentionDays = limit.RetentionDays;
            report.Messages.Add(CorpusStorageCheck.Describe(estimate, limit));

            if (refusal != null)
            {
                report.WellFormed = false;
                report.Messages.Add(refusal);
            }
        }

        if (!pool.TrySetValue(activity.ReturnReference, report))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusPreflight CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 3)
                throw new ArgumentException($"Expected 3 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference manifest)
                throw new ArgumentException("Parameter 'Manifest' must be a variable reference.");

            if (parameters[1] is not IWitReference languageModel)
                throw new ArgumentException("Parameter 'LanguageModel' must be a variable reference.");

            if (parameters[2] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityCorpusPreflight
            {
                Manifest = manifest,
                LanguageModel = languageModel,
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
