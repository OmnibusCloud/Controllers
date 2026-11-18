using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusSplit : WitActivityAdapterFunction<WitActivityCorpusSplit>
{
    #region Constants

    /// <summary>The most problems a refusal quotes: the preflight lists them; a refusal names the first.</summary>
    private const int QUOTED_PROBLEMS = 5;

    private static readonly Lazy<HttpClient> CLIENT = new(() => ShardHttp.CreateClient(ShardOriginPolicy.Machine));

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusSplit(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusSplit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var planned = await CorpusPlanning.PlanAsync(BlobService, pool, activity.Manifest, activity.LanguageModel, activity.Options, "Manifest");
        var plan = planned.Plan;
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        if (plan.ErrorCount > 0)
            throw new InvalidOperationException($"The manifest has {plan.ErrorCount} problem(s): {string.Join(" ", plan.Errors.Take(QUOTED_PROBLEMS))}");

        foreach (var note in plan.Notes)
            Logger.LogInformation("Corpus.Split: {Note}", note);

        // The origin's own word on a random sample of the shards: a shard the manifest gave no
        // size for gets one (its work estimate, and what a node holds it to), and a declared size
        // far below the origin's stops the job here, by name, instead of on the nodes.
        var sizes = await CorpusPlanning.SizeAsync(planned.Manifest, CLIENT.Value, cancellationToken);
        Logger.LogInformation("Corpus.Split: {Probe}", CorpusPlanning.DescribeProbe(sizes));

        var overdeclared = CorpusPlanning.Overdeclared(sizes);
        if (overdeclared.Count > 0)
            throw new InvalidOperationException($"The manifest has {overdeclared.Count} problem(s): {string.Join(" ", overdeclared.Take(QUOTED_PROBLEMS))}");

        // The preflight's storage check, enforced: a job the deployment has no room for does not start.
        var estimates = CorpusPlanning.Estimate(planned, sizes);
        var (limit, refusal) = await CorpusPlanning.CheckStorageAsync(estimates.Storage, new CorpusStorageHeadroomHost(BlobService), cancellationToken);
        if (refusal != null)
            throw new InvalidOperationException(refusal);

        Logger.LogInformation("Corpus.Split: {Storage}", CorpusStorageCheck.Describe(estimates.Storage, limit));

        // The first round: every task under a ticket of its own, on the one version the job runs
        // on, written into packages as it is made. What the script gets is the packages' references.
        var packages = await CorpusRounds.HostAsync("Corpus.Split", () => CorpusRounds.PackagesAsync(BlobService, CorpusTaskPlanner.Issue(plan, ControllerBuildInfo.VERSION, sizes), plan.Tasks.Count));

        Logger.LogInformation("Corpus.Split: {Shards} shard(s) in {Packages} task package(s), {Bytes} bytes of references' blobs.", plan.Tasks.Count, packages.Count, packages.Sum(package => package.SizeBytes));

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
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
