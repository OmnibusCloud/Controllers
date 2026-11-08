using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// Turns a parsed manifest into node tasks: one shard per task, every source checked against
/// the allowlist and the byte cap here, on the host, so a bad manifest never reaches a node;
/// the pipeline completed with the language model's transport and pin.
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
    /// <returns>The plan; tasks are empty when there are errors.</returns>
    public static CorpusPlan Plan(CorpusManifest manifest, CorpusOptionsData? options, Guid languageModelBlobId, string languageModelSha256)
    {
        var errors = new List<string>(manifest.Errors);
        var notes = new List<string>();
        var refused = new List<string>();
        var oversized = 0;
        long traffic = 0;

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
            }
        }
        else
        {
            pipeline.LanguageModelBlobId = Guid.Empty;
            pipeline.LanguageModelSha256 = "";
            if (pipeline.TargetLanguages.Count > 0)
                notes.Add("target_languages are ignored while identify_language is false.");
        }

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

            traffic += source.SizeBytes;
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

        var duplicates = manifest.Sources.GroupBy(source => source.Url, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        foreach (var duplicate in duplicates)
            notes.Add($"'{duplicate}' is listed more than once; each listing is a task of its own.");

        if (tasks.Count > 0)
            notes.Add($"{tasks.Count} shard(s), cap {cap} bytes each, estimated traffic {traffic} bytes.");

        return new CorpusPlan(errors.Count == 0 ? tasks : [], errors, notes, refused, oversized, traffic);
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
