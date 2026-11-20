using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// The host-side steps Split and Preflight share: read the manifest blob, hash and read the
/// language model blob when one is given (a model this controller cannot use is refused here,
/// before any task is sent), parse, plan; ask the origin for the sizes of a random sample of the
/// shards; and from what is then known of the sizes, estimate what the job leaves on the server
/// and what the contributors' links will carry.
/// </summary>
internal static class CorpusPlanning
{
    #region Functions

    /// <summary>Reads the inputs from the pool and plans the job.</summary>
    /// <param name="blobService">Resolves the blobs.</param>
    /// <param name="pool">The script's variables.</param>
    /// <param name="manifest">The manifest blob reference.</param>
    /// <param name="languageModel">The language model blob reference (unset or empty = none).</param>
    /// <param name="options">The options reference (unset = defaults).</param>
    /// <param name="manifestName">The manifest parameter's name for the error.</param>
    /// <returns>The plan with what it was made from.</returns>
    /// <exception cref="InvalidOperationException">The manifest blob is missing.</exception>
    public static async Task<CorpusPlanned> PlanAsync(IWitBlobService blobService, IWitVariablesCollection pool, IWitReference? manifest, IWitReference? languageModel, IWitReference? options, string manifestName)
    {
        if (!pool.TryGetValue(manifest, out Guid manifestBlobId) || manifestBlobId == Guid.Empty)
            throw new InvalidOperationException($"Failed to get parameter '{manifestName}'.");

        pool.TryGetValue(options, out CorpusOptionsData? optionsData);

        long modelBytes = 0;
        var modelBlobId = Guid.Empty;
        var modelSha256 = "";
        CorpusLanguageModelFacts? modelFacts = null;
        if (pool.TryGetValue(languageModel, out Guid modelBlob) && modelBlob != Guid.Empty)
        {
            var modelPath = await blobService.GetLocalPathAsync(modelBlob);
            modelBlobId = modelBlob;
            modelSha256 = ShardLanguageModels.Hash(modelPath);
            modelFacts = CorpusLanguageModelFacts.Load(modelPath);
            modelBytes = new FileInfo(modelPath).Length;
        }

        var manifestPath = await blobService.GetLocalPathAsync(manifestBlobId);
        var manifestBytes = new FileInfo(manifestPath).Length;

        // A manifest is a lab's input: its size is asked before a byte of it is read, and it is
        // read as it comes, a source at a time; its text is never held.
        var parsed = manifestBytes > CorpusManifestParser.MAX_BYTES
            ? new CorpusManifest(new CorpusPipelineData(), [], [], [$"manifest: {manifestBytes} bytes; a manifest is at most {CorpusManifestParser.MAX_BYTES} bytes. Split it into several jobs."])
            : await Task.Run(() => ParseFile(manifestPath));

        var plan = CorpusTaskPlanner.Plan(parsed, optionsData, modelBlobId, modelSha256, languageModel: modelFacts);

        return new CorpusPlanned(plan, parsed, optionsData, manifestBytes + modelBytes, modelFacts, plan.Pipeline.IdentifyLanguage ? modelBytes : 0);
    }

    /// <summary>
    /// Asks the origin for the sizes of a random sample of the shards. A probe that cannot be made
    /// at all leaves the manifest's own sizes standing: the nodes hold every shard to its size or
    /// to the cap either way.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="client">A client for this machine's policy.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <returns>What is known of every shard's size.</returns>
    public static async Task<ShardSizeProbeResult> SizeAsync(CorpusManifest manifest, HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var origins = manifest.AllowedOrigins.Where(ShardOrigin.IsAscii).Select(ShardOrigin.Normalize).Where(host => host.Length > 0).ToList();
            return await ShardSizeProbe.ProbeAsync(manifest.Sources, origins, ShardOriginPolicy.Machine, client, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ShardSizeProbe.Unasked(manifest.Sources);
        }
    }

    /// <summary>The problems a probe found: sources whose size at the origin is far above the one the manifest declares.</summary>
    /// <param name="sizes">What the probe learned.</param>
    /// <returns>One line per such source of the sample, by manifest index.</returns>
    public static List<string> Overdeclared(ShardSizeProbeResult sizes)
    {
        return sizes.Overdeclared
            .Select(mismatch => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"sources[{mismatch.Index}]: the origin answers {mismatch.OriginBytes} bytes and the manifest declares {mismatch.DeclaredBytes}: a node holds the shard to {ShardLimits.DeclaredCap(mismatch.DeclaredBytes)} bytes and would refuse it ({Trust.CorpusResultCodes.OVERSIZED_DECLARED}). Declare the shard's real size."))
            .ToList();
    }

    /// <summary>The probe's line for a preflight.</summary>
    /// <param name="sizes">What the probe learned.</param>
    /// <returns>The message.</returns>
    public static string DescribeProbe(ShardSizeProbeResult sizes)
    {
        var line = $"Sizes asked at the origin for a random sample: {sizes.Probed} source(s) without a size answered" +
                   (sizes.Failed > 0 ? $", {sizes.Failed} unanswered" : "") +
                   (sizes.Unprobed > 0 ? $", {sizes.Unprobed} not asked (beyond the probe budget, or a host this server does not fetch from)" : "") +
                   $"; {sizes.Verified} declared size(s) checked" +
                   (sizes.Overdeclared.Count > 0 ? $", {sizes.Overdeclared.Count} of them far below the origin's" : "") + ".";

        if (sizes.Unsized > 0)
            line += sizes.MeanBytes > 0
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" {sizes.Unsized} shard(s) have no size of their own: each is counted at the mean of the answers ({sizes.MeanBytes} bytes).")
                : $" {sizes.Unsized} shard(s) have no size of their own and no answer to estimate them from: each is counted at the cap.";

        return line;
    }

    /// <summary>Estimates what the job leaves on the server and what the contributors' links carry, from what is known of the sizes.</summary>
    /// <param name="planned">The plan and its inputs.</param>
    /// <param name="sizes">What is known of every shard's size.</param>
    /// <param name="nodes">The nodes that will take part; 0 when not known.</param>
    /// <returns>The estimates.</returns>
    public static CorpusEstimates Estimate(CorpusPlanned planned, ShardSizeProbeResult sizes, int nodes = 0)
    {
        var sources = planned.Manifest.Sources;
        var cap = CorpusTaskPlanner.ShardCap(planned.Options);
        var rate = planned.Options?.SamplingRate ?? 0;

        // A shard nobody knows the size of is counted at the cap: it is what a node holds it to.
        var counted = new long[sources.Count];
        var bounds = new long[sources.Count];
        var sized = new List<CorpusSourceData>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            counted[index] = Math.Min(sizes.Sizes[index] > 0 ? sizes.Sizes[index] : cap, cap);
            bounds[index] = Math.Min(cap, ShardLimits.DeclaredCap(sizes.Declared[index]));
            sized.Add(new CorpusSourceData { Format = sources[index].Format, SizeBytes = sizes.Sizes[index] > 0 ? Math.Min(sizes.Sizes[index], cap) : 0 });
        }

        var storage = CorpusStorageEstimator.Estimate(sized, planned.Plan.Pipeline, rate, planned.InputBytes, cap, planned.Model?.MeasuredThresholds ?? true, bounds);
        var uploadCeiling = storage.ProductCeilingBytes > long.MaxValue - storage.ResultPackageBytes ? long.MaxValue : storage.ProductCeilingBytes + storage.ResultPackageBytes;
        var traffic = CorpusTrafficEstimate.Of(counted, rate, bounds, storage.UploadBytes, planned.ModelBytes, nodes, uploadCeiling);

        return new CorpusEstimates(storage, traffic, counted) { Bounds = bounds };
    }

    /// <summary>Asks the deployment for the room one job has and holds an estimate to it.</summary>
    /// <param name="estimate">The job's storage estimate.</param>
    /// <param name="headroom">Where the deployment's limit comes from.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>The limit, and the refusal when the job does not fit (null when it does or no headroom is stated).</returns>
    public static async Task<(CorpusStorageLimit Limit, string? Refusal)> CheckStorageAsync(CorpusStorageEstimate estimate, ICorpusStorageHeadroom headroom, CancellationToken cancellationToken)
    {
        var limit = await headroom.GetAsync(cancellationToken);

        return (limit, CorpusStorageCheck.Refusal(estimate, limit));
    }

    /// <summary>Estimates what the job leaves on the server and asks the deployment for its limit.</summary>
    /// <param name="sources">The shards with their sizes (a size of 0 or less is unknown).</param>
    /// <param name="pipeline">The pipeline every shard runs through.</param>
    /// <param name="options">The job's options (null = defaults).</param>
    /// <param name="inputBytes">The bytes of the manifest and the language model.</param>
    /// <param name="headroom">Where the deployment's limit comes from.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    /// <returns>The estimate, the limit, and the refusal when the job does not fit (null when it does or no headroom is stated).</returns>
    public static async Task<(CorpusStorageEstimate Estimate, CorpusStorageLimit Limit, string? Refusal)> CheckStorageAsync(IReadOnlyList<CorpusSourceData> sources, CorpusPipelineData pipeline, CorpusOptionsData? options, long inputBytes, ICorpusStorageHeadroom headroom, CancellationToken cancellationToken)
    {
        var estimate = CorpusStorageEstimator.Estimate(sources, pipeline, options?.SamplingRate ?? 0, inputBytes, CorpusTaskPlanner.ShardCap(options));
        var (limit, refusal) = await CheckStorageAsync(estimate, headroom, cancellationToken);

        return (estimate, limit, refusal);
    }

    #endregion

    #region Tools

    private static CorpusManifest ParseFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return CorpusManifestParser.Parse(file);
    }

    #endregion
}
