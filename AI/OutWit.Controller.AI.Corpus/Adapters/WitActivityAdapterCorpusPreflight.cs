using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Logging;
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
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusPreflight activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var planned = await CorpusPlanning.PlanAsync(BlobService, pool, activity.Manifest, activity.LanguageModel, activity.Options, "Manifest");
        var plan = planned.Plan;
        var manifest = planned.Manifest;
        var options = planned.Options;
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        // Every list of the answer is bounded, whatever the manifest holds: the first problems by
        // line and a count of the rest, a bounded number of host names.
        var report = new CorpusPreflightData
        {
            WellFormed = plan.ErrorCount == 0 && plan.Tasks.Count > 0,
            TaskCount = plan.Tasks.Count,
            RefusedOrigins = [.. plan.RefusedOrigins],
            OversizedSources = plan.OversizedSources,
            Messages = [.. plan.Errors, .. plan.Notes]
        };

        if (manifest.Sources.Count > 0)
        {
            // The origin's own word on a random sample: sizes for sources the manifest gave none
            // for, and a check of the sizes it did give. A manifest with problems is not asked about.
            var sizes = plan.ErrorCount == 0
                ? await CorpusPlanning.SizeAsync(manifest, CLIENT.Value, cancellationToken)
                : ShardSizeProbe.Unasked(manifest.Sources);

            if (plan.ErrorCount == 0)
                report.Messages.Add(CorpusPlanning.DescribeProbe(sizes));

            var overdeclared = CorpusPlanning.Overdeclared(sizes);
            if (overdeclared.Count > 0)
            {
                report.WellFormed = false;
                report.Messages.AddRange(overdeclared);
            }

            // The busiest contributor's share at this fleet: a share and counts, never a node.
            var cap = CorpusTaskPlanner.ShardCap(options);
            var share = await PlanSharesAsync(sizes.Sizes, options?.SamplingRate ?? 0);

            // What the contributors' links carry and what the nodes enforce, from the same sizes.
            var estimates = CorpusPlanning.Estimate(planned, sizes, share?.PlannedNodes ?? 0);
            var traffic = estimates.Traffic;
            report.EstimatedTrafficBytes = traffic.TotalBytes;
            report.EstimatedFetchBytes = traffic.FetchBytes;
            report.EstimatedUploadBytes = traffic.UploadBytes;
            report.RetryAllowanceBytes = traffic.RetryBytes;
            report.FetchCeilingBytes = traffic.CeilingBytes;
            report.UnsizedSources = sizes.Unsized;
            report.Messages.Add(traffic.Describe());
            report.Messages.Add(traffic.DescribeCeiling(sizes.Unsized, cap));
            report.Messages.Add(traffic.DescribeRounds());

            // The disk a node needs: it holds a few shards at a time, never its whole share.
            var largest = sizes.Sizes.Count > 0 ? Math.Min(Math.Max(0, sizes.Sizes.Max()), cap) : 0;
            report.NodeDiskBytesPerShard = ShardDisk.Needed(largest > 0 ? largest : cap, plan.Pipeline);
            report.Messages.Add(ShardDisk.Describe(largest, cap, plan.Pipeline));

            // What the job leaves on the server, against the deployment's headroom.
            var (limit, refusal) = await CorpusPlanning.CheckStorageAsync(estimates.Storage, new CorpusStorageHeadroomHost(BlobService), cancellationToken);

            report.EstimatedStorageBytes = estimates.Storage.TotalBytes;
            report.StorageHeadroomBytes = limit.HeadroomBytes;
            report.StorageRetentionDays = limit.RetentionDays;
            report.Messages.Add(CorpusStorageCheck.Describe(estimates.Storage, limit));

            if (refusal != null)
            {
                report.WellFormed = false;
                report.Messages.Add(refusal);
            }

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
