using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Manifests;
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

    public WitActivityAdapterCorpusPreflight(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        Services = services;
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
            report.Messages.Add($"{probe.Probed} source size(s) probed at the origin" +
                                (probe.Failed > 0 ? $", {probe.Failed} unanswered" : "") +
                                (probe.Unprobed > 0 ? $", {probe.Unprobed} not asked (beyond the probe budget, or a host this server does not fetch from)" : "") +
                                (probe.Failed + probe.Unprobed > 0 ? " (estimated at the mean)" : "") + ".");
            if (probe.Sizes.Count == sizes.Count)
                sizes = [.. probe.Sizes];
        }

        // The traffic promise from the same sizes: every shard once, the sampled ones a second time.
        var traffic = CorpusTrafficEstimate.Of(sizes, options?.SamplingRate ?? 0);
        report.EstimatedTrafficBytes = traffic.TotalBytes;
        if (manifest.Sources.Count > 0)
            report.Messages.Add(traffic.Describe());

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

            // The busiest contributor's share at this fleet: a share and counts, never a node.
            var share = await PlanSharesAsync(sizes, options?.SamplingRate ?? 0);
            if (share != null)
            {
                report.BusiestNodeShare = share.BusiestShare;
                report.PlannedNodes = share.PlannedNodes;
                report.Messages.Add(share.Describe());
            }
        }

        if (!pool.TrySetValue(activity.ReturnReference, report))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Tools

    private async Task<CorpusSharePlan?> PlanSharesAsync(IReadOnlyList<long> sizes, double samplingRate)
    {
        // The nodes manager exists on a host only; a host that cannot answer leaves the share out.
        try
        {
            var nodes = await CorpusPoolProbe.RatesAsync(Services.GetService<IWitNodesManager>());
            return nodes == null ? null : CorpusSharePlanner.Plan(nodes, sizes, samplingRate);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Corpus.Preflight: the pool could not be asked; the busiest node's share is left out.");
            return null;
        }
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

    private IServiceProvider Services { get; }

    #endregion
}
