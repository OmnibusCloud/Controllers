using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusSplit : WitActivityAdapterFunction<WitActivityCorpusSplit>
{
    #region Constants

    private static readonly Lazy<HttpClient> CLIENT = new(() => ShardHttp.CreateClient(ShardOriginPolicy.Machine));

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusSplit(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusSplit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var (plan, _) = await CorpusPlanning.PlanAsync(BlobService, pool, activity.Manifest, activity.LanguageModel, activity.Options, "Manifest");

        if (plan.Errors.Count > 0)
            throw new InvalidOperationException($"The manifest has {plan.Errors.Count} problem(s): {string.Join(" ", plan.Errors.Take(5))}");

        foreach (var note in plan.Notes)
            Logger.LogInformation("Corpus.Split: {Note}", note);

        await FillSizesAsync(plan.Tasks, ProcessingManager.CancellationToken(status.JobId));

        if (!pool.TrySetCollection(activity.ReturnReference, plan.Tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>
    /// The work estimate of a task is its shard's size: sources the manifest left unsized are
    /// asked at the origin (HEAD, within the probe budget) and the rest take the mean of the
    /// answers, so the allocator weighs a 1 GB WARC file against a 60 MB WET file correctly.
    /// A failed probe leaves the size unknown; the node enforces the cap either way.
    /// </summary>
    private async Task FillSizesAsync(IReadOnlyList<CorpusTaskData> tasks, CancellationToken cancellationToken)
    {
        var sources = tasks.Select(task => task.Source!).ToList();
        if (sources.All(source => source.SizeBytes > 0))
            return;

        try
        {
            var origins = tasks.SelectMany(task => task.AllowedOrigins).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sizes = await ShardSizeProbe.ProbeSizesAsync(sources, origins, ShardOriginPolicy.Machine, CLIENT.Value, cancellationToken);
            var known = sizes.Where(size => size is > 0).Select(size => size!.Value).ToList();
            var mean = known.Count > 0 ? (long)known.Average() : 0;

            for (var index = 0; index < sources.Count; index++)
            {
                if (sources[index].SizeBytes <= 0)
                    sources[index].SizeBytes = sizes[index] ?? mean;
            }

            Logger.LogInformation("Corpus.Split: {Probed} shard size(s) from the origin, {Total:F1} MB in all", known.Count, sources.Sum(source => source.SizeBytes) / (1024.0 * 1024.0));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogWarning(exception, "Corpus.Split: the shard sizes could not be probed; the work estimate falls back to a typical shard.");
        }
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusSplit CreateActivity(IWitParameter[] parameters)
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

            return new WitActivityCorpusSplit
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
