using Microsoft.Extensions.Logging;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepPlan : WitActivityAdapterFunction<WitActivitySweepPlan>
{
    #region Constructors

    public WitActivityAdapterSweepPlan(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySweepPlan activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.BaseDeck, out Guid baseDeckBlobId) || baseDeckBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'BaseDeck'.");

        if (!pool.TryGetValue(activity.Options, out SweepOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get parameter 'Options'.");

        if (options.Variants.Count == 0)
            throw new InvalidOperationException("The sweep carries no variants.");

        foreach (var variant in options.Variants)
        {
            if (variant.Values.Count != options.Parameters.Count)
                throw new InvalidOperationException(
                    $"Variant #{variant.VariantIndex} carries {variant.Values.Count} value(s) for {options.Parameters.Count} parameter(s).");
        }

        // The manifest maps results by VariantIndex, never positionally — a
        // repeated index would burn the whole sweep's node-time and come
        // back permanently unmappable to variants. Plan is the one
        // validation gate, so the study identity is checked here.
        var duplicate = options.Variants
            .GroupBy(variant => variant.VariantIndex)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException(
                $"Variant index {duplicate.Key} appears {duplicate.Count()} time(s) — variant indices must be unique.");

        // A deck-set study (every variant brings its own ready deck) and a
        // templated study are distinct modes — mixing them would leave part
        // of the table silently unsolvable, so it rejects up front.
        var ownDeckCount = options.Variants.Count(variant => variant.DeckBlobId != Guid.Empty);
        if (ownDeckCount > 0 && ownDeckCount < options.Variants.Count)
            throw new InvalidOperationException(
                $"{ownDeckCount} of {options.Variants.Count} variant(s) carry their own deck — a study is either a deck set or a template, never both.");

        if (ownDeckCount > 0 && options.Parameters.Count > 0)
            throw new InvalidOperationException(
                "A deck-set study cannot also declare template parameters.");

        if (ownDeckCount == 0)
        {
            // The template is validated once, up front — a token missing from the
            // deck must reject the sweep before any node burns a solve on it.
            var deckPath = await BlobService.GetLocalPathAsync(baseDeckBlobId);
            var deckText = await File.ReadAllTextAsync(deckPath);
            SweepDeckTemplating.ValidateTemplate(deckText, options.Parameters);
        }

        var plan = new SweepPlanData
        {
            BaseDeckBlobId = baseDeckBlobId,
            Options = options,
            ChunkSizes = SweepChunkPlanner.Sizes(options.FirstChunkSize, options.MaxChunkSize, options.Variants.Count)
        };

        if (!pool.TrySetValue(activity.ReturnReference, plan))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepPlan CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference baseDeck)
                throw new ArgumentException("Parameter 'BaseDeck' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivitySweepPlan
            {
                BaseDeck = baseDeck,
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
