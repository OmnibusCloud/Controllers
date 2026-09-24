using Microsoft.Extensions.Logging;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepPlan : WitActivityAdapterFunction<WitActivitySweepPlan>
{
    #region Constructors

    public WitActivityAdapterSweepPlan(IWitProcessingManager processingManager, IWitBlobService blobService, IWitNodesManager nodesManager, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        NodesManager = nodesManager;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySweepPlan activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Options, out SweepOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get parameter 'Options'.");

        // Plan is the one validation gate: every finding of the study and of
        // its family block is named together, before any node sees a task.
        var findings = ValidateStudy(options);
        if (options.Family is { } family)
            findings.AddRange(await SweepFamilies.For(family).ValidateAsync(options, BlobService));

        if (findings.Count > 0)
            throw new InvalidOperationException($"The sweep is refused: {string.Join(" ", findings)}");

        // The fleet width: every machine that may take a task of this job, local nodes and the
        // handles other clouds offer alike. A chunk is a wave, so a chunk narrower than the
        // fleet leaves machines idle; the planner raises the first chunk to the width.
        var availableNodes = await CountAvailableNodesAsync();

        var plan = new SweepPlanData
        {
            Options = options,
            ChunkSizes = SweepChunkPlanner.Sizes(options.FirstChunkSize, options.MaxChunkSize, options.Variants.Count, availableNodes)
        };

        Logger.LogInformation(
            "Sweep plan: {Family}, {Variants} variant(s), {Nodes} eligible machine(s), chunks [{Chunks}] (client asked first {First}, max {Max})",
            options.Family, options.Variants.Count, availableNodes, string.Join(", ", plan.ChunkSizes), options.FirstChunkSize, options.MaxChunkSize);

        if (!pool.TrySetValue(activity.ReturnReference, plan))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>
    /// The family-independent rules of a study: one family block, at least one
    /// variant, one value per parameter in every variant, and unique variant
    /// indices - the manifest maps results by index, never positionally, so a
    /// repeated index would burn node time and come back unmappable.
    /// </summary>
    private static List<string> ValidateStudy(SweepOptionsData options)
    {
        var findings = new List<string>();

        if (options.Family == null)
        {
            findings.Add(options.CalculiX == null && options.OpenFOAM == null
                ? "The study carries no family block (CalculiX or OpenFOAM)."
                : "The study carries more than one family block; a study runs on one solver family.");
        }

        if (options.Variants.Count == 0)
            findings.Add("The study carries no variants.");

        foreach (var variant in options.Variants.Where(variant => variant.Values.Count != options.Parameters.Count))
            findings.Add($"Variant #{variant.VariantIndex} carries {variant.Values.Count} value(s) for {options.Parameters.Count} parameter(s).");

        foreach (var group in options.Variants.GroupBy(variant => variant.VariantIndex).Where(group => group.Count() > 1))
            findings.Add($"Variant index {group.Key} appears {group.Count()} times; variant indices must be unique.");

        return findings;
    }

    /// <summary>
    /// How many machines could take a task of this job right now: the engine's compatible
    /// node query for the job itself (required controllers, the rollout fence, schedules and
    /// remote handles all apply). Zero when the query is unavailable, which leaves the plan as
    /// the client asked.
    /// </summary>
    private async Task<int> CountAvailableNodesAsync()
    {
        try
        {
            var nodes = await NodesManager.GetCompatibleNodes(typeof(object), WitProcessingOptions.Default);
            return nodes.Count;
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Sweep plan: the eligible machine count is unavailable; the chunk plan stays as the client asked");
            return 0;
        }
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepPlan CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivitySweepPlan
            {
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

    private IWitNodesManager NodesManager { get; }

    #endregion
}
