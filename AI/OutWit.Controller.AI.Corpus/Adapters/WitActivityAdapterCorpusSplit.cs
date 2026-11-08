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

        if (!pool.TrySetCollection(activity.ReturnReference, plan.Tasks))
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
