using System.Text;
using Microsoft.Extensions.Logging;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepMakeChunk : WitActivityAdapterFunction<WitActivitySweepMakeChunk>
{
    #region Constructors

    public WitActivityAdapterSweepMakeChunk(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySweepMakeChunk activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan?.Options == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SweepStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (state.ChunkIndex >= plan.ChunkSizes.Count)
            throw new InvalidOperationException(
                $"Chunk {state.ChunkIndex} requested, but the plan holds {plan.ChunkSizes.Count} chunk(s).");

        var options = plan.Options;
        var chunkSize = plan.ChunkSizes[state.ChunkIndex];
        var variants = options.Variants
            .Skip(state.NextVariantOrdinal)
            .Take(chunkSize)
            .ToList();

        // Variant decks materialize lazily, one chunk at a time — a 300-variant
        // night sweep never holds 300 deck blobs at once. A deck-set variant
        // (UC-3) brings its own uploaded deck, so its chunk is pure metadata:
        // the base template loads only when a templated variant needs it.
        string? deckText = null;

        var tasks = new List<CcxTaskData>(variants.Count);
        foreach (var variant in variants)
        {
            Guid deckBlobId;
            if (variant.DeckBlobId != Guid.Empty)
            {
                deckBlobId = variant.DeckBlobId;
            }
            else
            {
                if (deckText == null)
                {
                    var deckPath = await BlobService.GetLocalPathAsync(plan.BaseDeckBlobId);
                    deckText = await File.ReadAllTextAsync(deckPath);
                }

                var variantDeck = SweepDeckTemplating.Instantiate(deckText, options.Parameters, variant.Values);
                deckBlobId = await BlobService.UploadBytesAsync(
                    Encoding.UTF8.GetBytes(variantDeck),
                    $"variant_{variant.VariantIndex}.inp");
            }

            tasks.Add(new CcxTaskData
            {
                VariantIndex = variant.VariantIndex,
                DeckBlobId = deckBlobId,
                NodeCount = variant.NodeCount > 0 ? variant.NodeCount : options.NodeCount,
                ElementCount = variant.ElementCount > 0 ? variant.ElementCount : options.ElementCount,
                Threads = options.Threads,
                Extraction = options.Extraction
            });
        }

        if (!pool.TrySetCollection(activity.ReturnReference, tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepMakeChunk CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivitySweepMakeChunk
            {
                Plan = plan,
                State = state
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
