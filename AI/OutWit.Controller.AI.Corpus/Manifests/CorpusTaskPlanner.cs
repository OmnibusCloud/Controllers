using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// Turns a parsed manifest into node tasks: one shard per task, every source checked against
/// the allowlist and the byte cap here, on the host, so a bad manifest never reaches a node;
/// the pipeline completed with the language model's transport and pin. A host that is not a
/// public origin does not stop the plan: whether to fetch from it is each machine's own
/// decision, so the plan names such hosts and the machines answer for themselves.
/// </summary>
public static class CorpusTaskPlanner
{
    #region Constants

    /// <summary>The controller's own cap on a shard: 1 GiB on the wire, whatever the options ask.</summary>
    public const long MAX_SHARD_BYTES = 1L << 30;

    #endregion

    #region Functions

    /// <summary>Plans the tasks.</summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="options">Job options (null = defaults).</param>
    /// <param name="languageModelBlobId">The blob carrying the language model, or Guid.Empty.</param>
    /// <param name="languageModelSha256">Lower-case hex SHA-256 of that blob's bytes, or "".</param>
    /// <param name="policy">What the planning machine fetches from (null = this machine's policy).</param>
    /// <param name="languageModel">What the host read in the language model blob (null = not read): a model this controller cannot use stops the plan.</param>
    /// <returns>The plan; tasks are empty when there are errors.</returns>
    public static CorpusPlan Plan(CorpusManifest manifest, CorpusOptionsData? options, Guid languageModelBlobId, string languageModelSha256, ShardOriginPolicy? policy = null, CorpusLanguageModelFacts? languageModel = null)
    {
        policy ??= ShardOriginPolicy.Machine;

        var errors = new List<string>(manifest.Errors);
        var notes = new List<string>();
        var refused = new List<string>();
        var outside = new List<string>();
        var oversized = 0;

        var cap = ShardCap(options);
        var origins = manifest.AllowedOrigins.Select(ShardOrigin.Normalize).Where(host => host.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (origins.Count == 0 && manifest.Errors.Count == 0)
            errors.Add("allowed_origins: at least one host is required.");

        var pipeline = manifest.Pipeline.Clone();
        if (pipeline.IdentifyLanguage)
        {
            if (languageModelBlobId == Guid.Empty)
                errors.Add("pipeline: the language stage is on but no language model was given.");
            else if (pipeline.LanguageModelSha256.Length > 0 && !string.Equals(pipeline.LanguageModelSha256, languageModelSha256, StringComparison.Ordinal))
                errors.Add($"pipeline: the language model's hash {languageModelSha256} does not match the pinned {pipeline.LanguageModelSha256}.");
            else
            {
                pipeline.LanguageModelBlobId = languageModelBlobId;
                pipeline.LanguageModelSha256 = languageModelSha256;
                if (languageModel != null)
                    CheckLanguageModel(languageModel, pipeline, errors, notes);
            }
        }
        else
        {
            pipeline.LanguageModelBlobId = Guid.Empty;
            pipeline.LanguageModelSha256 = "";
            if (pipeline.TargetLanguages.Count > 0)
                notes.Add("target_languages are ignored while identify_language is false.");
        }

        DescribeProducts(pipeline, notes);

        var tasks = new List<CorpusTaskData>(manifest.Sources.Count);
        for (var index = 0; index < manifest.Sources.Count; index++)
        {
            var source = manifest.Sources[index];
            var refusal = ShardOrigin.Check(source.Url, origins, out var uri);
            if (refusal != null)
            {
                errors.Add($"sources[{index}]: {refusal}");
                if (Uri.TryCreate(source.Url, UriKind.Absolute, out var parsed) && !refused.Contains(parsed.IdnHost, StringComparer.OrdinalIgnoreCase))
                    refused.Add(parsed.IdnHost);
            }
            else if (uri != null && policy.Check(uri) is { } unfit)
            {
                // A public origin written the wrong way is wrong on every machine; any other
                // host is for each machine's owner to decide.
                if (ShardOriginPolicy.IsPublicOrigin(uri.IdnHost))
                    errors.Add($"sources[{index}]: {unfit}");
                else if (!outside.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
                    outside.Add(uri.IdnHost);
            }

            if (source.SizeBytes > cap)
            {
                oversized++;
                errors.Add($"sources[{index}]: the estimated size {source.SizeBytes} exceeds the cap of {cap} bytes.");
            }

            tasks.Add(new CorpusTaskData
            {
                TaskIndex = index,
                Source = source.Clone(),
                Pipeline = pipeline.Clone(),
                AllowedOrigins = [.. origins],
                MaxShardBytes = cap
            });
        }

        // Every shard once, and the shards sampled for integrity a second time (the largest, as the storage estimate takes them).
        var traffic = CorpusTrafficEstimate.Of(manifest.Sources.Select(source => source.SizeBytes).ToList(), options?.SamplingRate ?? 0);

        var duplicates = manifest.Sources.GroupBy(source => source.Url, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        foreach (var duplicate in duplicates)
            notes.Add($"'{duplicate}' is listed more than once; each listing is a task of its own.");

        if (tasks.Count > 0)
            notes.Add($"{tasks.Count} shard(s), cap {cap} bytes each, estimated traffic {traffic.TotalBytes} bytes" +
                      (traffic.SampledShards > 0 ? $" ({traffic.CopyBytes} of them the second fetch of the {traffic.SampledShards} largest shard(s), sampled for integrity)." : "."));

        if (outside.Count > 0)
            notes.Add($"{string.Join(", ", outside)}: not a public origin ({string.Join(", ", ShardOriginPolicy.PUBLIC_ORIGINS)}). " +
                      $"Shards from there run only on machines whose owner lists the host in {ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE}; every other machine answers OriginRefused.");

        return new CorpusPlan(errors.Count == 0 ? tasks : [], errors, notes, refused, outside, oversized, traffic.TotalBytes);
    }

    /// <summary>
    /// What the run keeps on the server and whether its signatures can be merged: a run that keeps
    /// statistics and hashes only, or does not sign, has no signatures for Corpus.Dedup; MinHash
    /// parameters other than the defaults are named, since signatures made otherwise do not merge with them.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="notes">Receives the lines.</param>
    private static void DescribeProducts(CorpusPipelineData pipeline, List<string> notes)
    {
        var minHash = MinHashParameters.Resolve(pipeline.MinHash);
        if (pipeline.Output == CorpusOutputMode.StatsAndHashes)
        {
            notes.Add($"output {CorpusOutputModeNames.STATS_AND_HASHES}: each shard is processed and hashed in full, but its text and signatures stay on its node and are deleted with the task; " +
                      "the server keeps the statistics, the hashes of the text and of the signatures, the report and the integrity document. " +
                      "Corpus.Dedup cannot run over this run: the near-duplicate merge needs the signatures themselves.");
        }
        else if (!pipeline.Sign)
            notes.Add("sign is false: no signatures; Corpus.Dedup cannot run over this run: the near-duplicate merge needs the signatures.");
        else if (!minHash.IsDefault)
            notes.Add($"signatures: {minHash.Describe()} (the defaults: {MinHashParameters.DEFAULT.Describe()}); Corpus.Dedup merges them only with signatures made the same way.");

        if (pipeline.MinHash != null && !ShardProcessor.Signs(pipeline))
            notes.Add("pipeline.minhash is ignored: the run makes no signatures.");
    }

    /// <summary>The language model as the host read it: refused by name when this controller cannot use it, described when it can.</summary>
    /// <param name="model">What the host read.</param>
    /// <param name="pipeline">The pipeline (its target languages).</param>
    /// <param name="errors">Receives the refusal.</param>
    /// <param name="notes">Receives the description and target languages the model does not have.</param>
    private static void CheckLanguageModel(CorpusLanguageModelFacts model, CorpusPipelineData pipeline, List<string> errors, List<string> notes)
    {
        if (model.Problem != null)
        {
            errors.Add($"pipeline: this controller cannot use the language model: {model.Problem} (it reads {string.Join(" and ", Language.LanguageModelFormat.READ)}).");
            return;
        }

        notes.Add(model.Format == Language.LanguageModelFormat.WITLID1
            ? $"language model: {model.Format}, {model.Languages.Count} language(s); every document is labelled with its nearest one (no unsupported-language test)."
            : $"language model: {model.Format} pack '{model.Name}', {model.Languages.Count} supported language(s) ({string.Join(" ", model.Languages)}), {model.KnownLanguages} more it knows and drops as language_unsupported.");

        var missing = pipeline.TargetLanguages.Where(code => !model.Languages.Contains(code, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
            notes.Add($"target_languages: {string.Join(", ", missing)} not among the model's languages; no document is labelled so.");
    }

    /// <summary>The byte cap in force: the options' cap, bounded by the controller's.</summary>
    /// <param name="options">Job options (null = defaults).</param>
    /// <returns>The cap.</returns>
    public static long ShardCap(CorpusOptionsData? options)
    {
        return options is { MaxShardBytes: > 0 } ? Math.Min(options.MaxShardBytes, MAX_SHARD_BYTES) : MAX_SHARD_BYTES;
    }

    #endregion
}
