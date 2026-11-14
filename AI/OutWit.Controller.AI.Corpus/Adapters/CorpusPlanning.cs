using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// The host-side steps Split and Preflight share: read the manifest blob, hash the language
/// model blob when one is given, parse, plan; and once the shard sizes are known, estimate
/// what the job leaves on the server and hold it to the deployment's headroom.
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
    /// <returns>The plan, the parsed manifest it came from, the options (null = defaults) and the bytes of the inputs (manifest and model) in the store.</returns>
    /// <exception cref="InvalidOperationException">The manifest blob is missing.</exception>
    public static async Task<(CorpusPlan Plan, CorpusManifest Manifest, CorpusOptionsData? Options, long InputBytes)> PlanAsync(IWitBlobService blobService, IWitVariablesCollection pool, IWitReference? manifest, IWitReference? languageModel, IWitReference? options, string manifestName)
    {
        if (!pool.TryGetValue(manifest, out Guid manifestBlobId) || manifestBlobId == Guid.Empty)
            throw new InvalidOperationException($"Failed to get parameter '{manifestName}'.");

        pool.TryGetValue(options, out CorpusOptionsData? optionsData);

        long inputBytes = 0;
        var modelBlobId = Guid.Empty;
        var modelSha256 = "";
        if (pool.TryGetValue(languageModel, out Guid modelBlob) && modelBlob != Guid.Empty)
        {
            var modelPath = await blobService.GetLocalPathAsync(modelBlob);
            modelBlobId = modelBlob;
            modelSha256 = ShardLanguageModels.Hash(modelPath);
            inputBytes += new FileInfo(modelPath).Length;
        }

        var manifestPath = await blobService.GetLocalPathAsync(manifestBlobId);
        var json = await File.ReadAllTextAsync(manifestPath);
        var parsed = CorpusManifestParser.Parse(json);
        inputBytes += new FileInfo(manifestPath).Length;

        return (CorpusTaskPlanner.Plan(parsed, optionsData, modelBlobId, modelSha256), parsed, optionsData, inputBytes);
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
        var estimate = CorpusStorageEstimator.Estimate(sources, pipeline, options?.SamplingRate ?? 0, inputBytes);
        var limit = await headroom.GetAsync(cancellationToken);

        return (estimate, limit, CorpusStorageCheck.Refusal(estimate, limit));
    }

    #endregion
}
