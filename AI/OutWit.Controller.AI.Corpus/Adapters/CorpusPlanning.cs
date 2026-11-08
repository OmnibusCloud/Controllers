using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// The host-side steps Split and Preflight share: read the manifest blob, hash the language
/// model blob when one is given, parse, plan.
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
    /// <returns>The plan and the parsed manifest it came from.</returns>
    /// <exception cref="InvalidOperationException">The manifest blob is missing.</exception>
    public static async Task<(CorpusPlan Plan, CorpusManifest Manifest)> PlanAsync(IWitBlobService blobService, IWitVariablesCollection pool, IWitReference? manifest, IWitReference? languageModel, IWitReference? options, string manifestName)
    {
        if (!pool.TryGetValue(manifest, out Guid manifestBlobId) || manifestBlobId == Guid.Empty)
            throw new InvalidOperationException($"Failed to get parameter '{manifestName}'.");

        pool.TryGetValue(options, out CorpusOptionsData? optionsData);

        var modelBlobId = Guid.Empty;
        var modelSha256 = "";
        if (pool.TryGetValue(languageModel, out Guid modelBlob) && modelBlob != Guid.Empty)
        {
            modelBlobId = modelBlob;
            modelSha256 = ShardLanguageModels.Hash(await blobService.GetLocalPathAsync(modelBlob));
        }

        var json = await File.ReadAllTextAsync(await blobService.GetLocalPathAsync(manifestBlobId));
        var parsed = CorpusManifestParser.Parse(json);

        return (CorpusTaskPlanner.Plan(parsed, optionsData, modelBlobId, modelSha256), parsed);
    }

    #endregion
}
