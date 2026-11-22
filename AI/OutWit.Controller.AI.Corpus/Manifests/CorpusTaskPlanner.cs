using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// Turns a parsed manifest into node tasks: one shard per task, every source checked against
/// the allowlist and the byte cap here, on the host, so a bad manifest never reaches a node;
/// the pipeline completed with the language model's transport and pin. A host that is not a
/// public origin does not stop the plan: whether to fetch from it is each machine's own
/// decision, so the plan names such hosts and the machines answer for themselves. A host that is
/// not written in ASCII does stop it: such a name is mapped to its wire form by each machine's
/// own library, and two machines need not map it alike.
/// </summary>
public static class CorpusTaskPlanner
{
    #region Constants

    /// <summary>The controller's own cap on a shard: 1 GiB on the wire, whatever the options ask.</summary>
    public const long MAX_SHARD_BYTES = 1L << 30;

    /// <summary>The most host names a plan lists (refused hosts, hosts outside the public origins): a list a manifest cannot grow.</summary>
    public const int MAX_LISTED_HOSTS = 20;

    /// <summary>The most duplicate listings a plan names.</summary>
    public const int MAX_LISTED_DUPLICATES = 10;

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

        var errors = new CorpusProblems();
        errors.Add(manifest.Errors, manifest.ErrorCount);
        var notes = new List<string>();
        var refused = new List<string>();
        var outside = new List<string>();
        var oversized = 0;
        bool refusedMore = false, outsideMore = false;

        var cap = ShardCap(options);
        foreach (var origin in manifest.AllowedOrigins)
        {
            if (!ShardOrigin.IsAscii(origin))
                errors.Add($"allowed_origins: '{ShardOrigin.Shown(origin)}' is not written in ASCII; write the host in its ASCII form (xn--...).");
        }

        var origins = manifest.AllowedOrigins.Where(ShardOrigin.IsAscii).Select(ShardOrigin.Normalize).Where(host => host.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (origins.Count == 0 && manifest.ErrorCount == 0 && errors.Count == 0)
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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new CorpusProblems(MAX_LISTED_DUPLICATES);
        for (var index = 0; index < manifest.Sources.Count; index++)
        {
            var source = manifest.Sources[index];
            var refusal = ShardOrigin.Check(source.Url, origins, out var uri);
            if (refusal != null)
            {
                errors.Add($"sources[{index}]: {refusal}");
                if (Uri.TryCreate(source.Url, UriKind.Absolute, out var parsed))
                    Note(refused, ShardOrigin.Shown(parsed.IdnHost), ref refusedMore);
            }
            else if (uri != null && policy.Check(uri) is { } unfit)
            {
                // A public origin written the wrong way is wrong on every machine; any other
                // host is for each machine's owner to decide.
                if (ShardOriginPolicy.IsPublicOrigin(uri.IdnHost))
                    errors.Add($"sources[{index}]: {unfit}");
                else
                    Note(outside, uri.IdnHost, ref outsideMore);
            }

            if (source.SizeBytes > cap)
            {
                oversized++;
                errors.Add($"sources[{index}]: the declared size {source.SizeBytes} exceeds the cap of {cap} bytes.");
            }

            if (!seen.Add(source.Url))
                duplicates.Add($"'{ShardOrigin.Shown(source.Url)}' is listed more than once; each listing is a task of its own.");
        }

        // Every shard once, and the packages sampled for integrity a second time (the largest, as the storage estimate takes them).
        var traffic = CorpusTrafficEstimate.Of(manifest.Sources.Select(source => source.SizeBytes).ToList(), options?.SamplingRate ?? 0);

        notes.AddRange(duplicates.ToList("duplicate listing"));

        if (manifest.Sources.Count > 0)
        {
            var perPackage = CorpusPackagePlanner.ShardsPerPackage(manifest.Sources.Count);
            notes.Add($"{manifest.Sources.Count} shard(s), cap {cap} bytes each, fetches by the manifest's own sizes {traffic.FetchBytes} bytes" +
                      (traffic.SampledPackages > 0 ? $" ({traffic.CopyBytes} of them the second fetch of the {traffic.SampledShards} largest shard(s), sampled for integrity)." : "."));

            if (perPackage > 1)
                notes.Add($"The shards travel in {CorpusPackagePlanner.Packages(manifest.Sources.Count)} packages of up to {perPackage} consecutive shards: the platform assigns a package to a node and samples a package for integrity as a whole (a job of up to {CorpusPackagePlanner.MAX_PACKAGES} shards has one shard a package).");
        }

        if (outside.Count > 0)
            notes.Add($"{string.Join(", ", outside)}{(outsideMore ? " and other hosts" : "")}: not a public origin ({string.Join(", ", ShardOriginPolicy.PUBLIC_ORIGINS)}). " +
                      $"Shards from there run only on machines whose owner lists the host in {ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE}; every other machine answers OriginRefused.");

        var planned = errors.Count == 0;
        return new CorpusPlan(planned ? new CorpusPlanTasks(manifest.Sources, pipeline, origins, cap) : [], errors.ToList(), notes, refused, outside, oversized, traffic.FetchBytes)
        {
            ErrorCount = errors.Count,
            Pipeline = pipeline,
            MaxShardBytes = cap
        };
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
    private static void CheckLanguageModel(CorpusLanguageModelFacts model, CorpusPipelineData pipeline, CorpusProblems errors, List<string> notes)
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
            notes.Add($"target_languages: {string.Join(", ", missing.Take(MAX_LISTED_HOSTS))}{(missing.Count > MAX_LISTED_HOSTS ? $" and {missing.Count - MAX_LISTED_HOSTS} more" : "")} not among the model's languages; no document is labelled so.");
    }

    /// <summary>
    /// The byte cap a node holds a shard to, whatever its task says: the task's cap when it is
    /// within the controller's ceiling, the ceiling otherwise (a task with no cap, or one above
    /// the ceiling, was not made by <c>Corpus.Split</c>).
    /// </summary>
    /// <param name="taskMaxShardBytes">The cap the task carries.</param>
    /// <returns>The cap in force, 1 to <see cref="MAX_SHARD_BYTES"/>.</returns>
    public static long NodeShardCap(long taskMaxShardBytes)
    {
        return taskMaxShardBytes is > 0 and <= MAX_SHARD_BYTES ? taskMaxShardBytes : MAX_SHARD_BYTES;
    }

    /// <summary>
    /// Makes planned tasks a job's first round: every task gets a ticket of its own, drawn at
    /// random, and the controller version the job runs on.
    /// </summary>
    /// <param name="tasks">The planned tasks; changed in place.</param>
    /// <param name="controllerVersion">The server's controller version.</param>
    public static void Issue(IEnumerable<CorpusTaskData> tasks, string controllerVersion)
    {
        foreach (var task in tasks)
        {
            task.Ticket = Trust.CorpusTickets.New();
            task.RequiredControllerVersion = controllerVersion;
        }
    }

    /// <summary>
    /// A plan's tasks as a job's first round, one at a time: each under a ticket of its own, on
    /// the controller version the job runs on, counted at the size the server learned for its
    /// shard and held to the size it was declared with.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="controllerVersion">The server's controller version.</param>
    /// <param name="sizes">What the server knows of the shards' sizes (the manifest's, the origin's answers); null for the manifest's own.</param>
    /// <returns>The tasks in manifest order, each made as it is asked for.</returns>
    public static IEnumerable<CorpusTaskData> Issue(CorpusPlan plan, string controllerVersion, ShardSizeProbeResult? sizes = null)
    {
        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var task = plan.Tasks[index];
            task.Ticket = Trust.CorpusTickets.New();
            task.RequiredControllerVersion = controllerVersion;

            if (sizes != null && index < sizes.Sizes.Count && task.Source != null)
            {
                // The work estimate is the size the server learned; the claim is the declared or the answered one.
                task.Source = new CorpusSourceData { Url = task.Source.Url, Format = task.Source.Format, SizeBytes = sizes.Sizes[index], Sha256 = task.Source.Sha256 };
                task.DeclaredBytes = sizes.Declared[index];
            }

            yield return task;
        }
    }

    /// <summary>The byte cap in force: the options' cap, bounded by the controller's.</summary>
    /// <param name="options">Job options (null = defaults).</param>
    /// <returns>The cap.</returns>
    public static long ShardCap(CorpusOptionsData? options)
    {
        return options is { MaxShardBytes: > 0 } ? Math.Min(options.MaxShardBytes, MAX_SHARD_BYTES) : MAX_SHARD_BYTES;
    }

    #endregion

    #region Tools

    /// <summary>Adds a host to a bounded list of names, once; past the bound the list only says there are more.</summary>
    private static void Note(List<string> hosts, string host, ref bool more)
    {
        if (hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            return;

        if (hosts.Count < MAX_LISTED_HOSTS)
            hosts.Add(host);
        else
            more = true;
    }

    #endregion
}
