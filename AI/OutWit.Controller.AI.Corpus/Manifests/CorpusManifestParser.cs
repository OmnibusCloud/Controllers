using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// Parses the manifest a lab submits: one JSON document with <c>pipeline</c>,
/// <c>allowed_origins</c> and <c>sources</c> (a URL string or an object with <c>url</c>,
/// <c>format</c>, <c>size</c>, <c>sha256</c>). Strict about names — an unknown key is an
/// error, so a typo cannot silently turn a stage off — and about types. The format of a bare
/// URL follows its extension: <c>.warc.wet.gz</c> is WET, <c>.warc.gz</c> is WARC. What it
/// answers with is bounded whatever the manifest holds: the first problems by line and a count of
/// the rest (<see cref="CorpusProblems"/>), and no more sources than a job may have
/// (<see cref="CorpusPackagePlanner.MAX_SHARDS"/>).
/// </summary>
public static class CorpusManifestParser
{
    #region Constants

    /// <summary>The most bytes a manifest may be: a manifest past it is refused by name before a byte of it is read.</summary>
    public const long MAX_BYTES = 256L * 1024 * 1024;

    private static readonly HashSet<string> ROOT_KEYS = new(StringComparer.Ordinal) { "pipeline", "allowed_origins", "sources" };
    private static readonly HashSet<string> PIPELINE_KEYS = new(StringComparer.Ordinal) { "version", "language_model_sha256", "target_languages", "identify_language", "clean_lines", "filter_quality", "sign", "extract_pdf", "quality", "filter_curly_bracket", "minhash", "output" };
    private static readonly HashSet<string> MINHASH_KEYS = new(StringComparer.Ordinal) { "hashes", "shingle_words", "bands" };
    private static readonly HashSet<string> SOURCE_KEYS = new(StringComparer.Ordinal) { "url", "format", "size", "sha256" };
    private static readonly HashSet<string> QUALITY_KEYS = new(StringComparer.Ordinal)
    {
        "min_words", "max_words", "min_mean_word_length", "max_mean_word_length", "max_symbol_to_word_ratio",
        "max_bullet_line_ratio", "max_ellipsis_line_ratio", "min_alphabetic_word_ratio", "max_duplicate_line_ratio", "max_duplicate_line_char_ratio"
    };

    #endregion

    #region Functions

    /// <summary>Parses a manifest document.</summary>
    /// <param name="json">The document text.</param>
    /// <returns>The manifest, with every problem listed in its errors.</returns>
    public static CorpusManifest Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF')));
        return Parse(stream);
    }

    /// <summary>
    /// Parses a manifest document from a stream. The document is read as it comes, a source at a
    /// time: its text is never held, only what it says (the pipeline, the origins, and for every
    /// source its URL, format, size and hash).
    /// </summary>
    /// <param name="stream">The document, UTF-8.</param>
    /// <returns>The manifest, with every problem listed in its errors.</returns>
    public static CorpusManifest Parse(Stream stream)
    {
        return Parse(stream, CorpusPackagePlanner.MAX_SHARDS);
    }

    /// <summary>Parses a manifest document from a stream, keeping at most a given number of sources (the bound of a job, lower in a test of the bound itself).</summary>
    /// <param name="stream">The document, UTF-8.</param>
    /// <param name="maxShards">The most sources kept; a manifest that names more is refused by name.</param>
    /// <returns>The manifest, with every problem listed in its errors.</returns>
    internal static CorpusManifest Parse(Stream stream, int maxShards)
    {
        // The problems in the order a reader of the manifest expects them, whatever the order of
        // the document's keys: its keys, its pipeline, its origins, its sources.
        var keys = new CorpusProblems();
        var pipelineErrors = new CorpusProblems();
        var originErrors = new CorpusProblems();
        var sourceErrors = new CorpusProblems();

        var pipeline = new CorpusPipelineData();
        var origins = new List<string>();
        var sources = new List<CorpusSourceData>();
        bool hasPipeline = false, hasOrigins = false, hasSources = false;
        var listed = 0;

        try
        {
            var listedAsArray = CorpusJsonStream.Read(stream, "sources",
                (name, value) =>
                {
                    switch (name)
                    {
                        case "pipeline":
                            hasPipeline = true;
                            pipeline = ParsePipeline(value, pipelineErrors);
                            break;
                        case "allowed_origins":
                            hasOrigins = true;
                            origins = ParseStrings(value, "allowed_origins", originErrors);
                            break;
                        case "sources":
                            hasSources = true;
                            sourceErrors.Add("sources: must be an array.");
                            break;
                        default:
                            keys.Add($"manifest: unknown key '{name}'.");
                            break;
                    }
                },
                (index, item) =>
                {
                    listed = index + 1;

                    // A job takes a bounded number of shards: past the bound nothing more is kept, only counted.
                    if (index < maxShards)
                        ParseSourceItem(item, index, sources, sourceErrors);
                },
                new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            hasSources |= listedAsArray;
        }
        catch (JsonException exception)
        {
            // The reader's own words for a document that is no object and for text that is no
            // Unicode; the JSON library's for everything else.
            var problem = exception.Message switch
            {
                CorpusJsonStream.NOT_AN_OBJECT => "manifest: the document must be a JSON object.",
                CorpusJsonStream.NOT_UNICODE => "manifest: a string or a key is not Unicode text (an escape of half a surrogate pair, or bytes that are not UTF-8).",
                _ => $"manifest: not valid JSON ({exception.Message})."
            };

            return new CorpusManifest(new CorpusPipelineData(), [], [], [problem]);
        }

        var errors = new CorpusProblems();
        errors.Add(keys.Listed, keys.Count);

        if (!hasPipeline)
            errors.Add("manifest: 'pipeline' is required.");
        errors.Add(pipelineErrors.Listed, pipelineErrors.Count);

        if (!hasOrigins)
            errors.Add("manifest: 'allowed_origins' is required.");
        errors.Add(originErrors.Listed, originErrors.Count);

        if (!hasSources)
            errors.Add("manifest: 'sources' is required.");
        errors.Add(sourceErrors.Listed, sourceErrors.Count);

        if (listed > maxShards)
            errors.Add($"sources: the manifest names {listed} shards; a job takes at most {maxShards}. Split the manifest into several jobs.");

        if (hasSources && sources.Count == 0 && errors.Count == 0)
            errors.Add("sources: the manifest names no shard.");

        if (string.IsNullOrWhiteSpace(pipeline.Version))
            errors.Add("pipeline: 'version' is required.");

        return new CorpusManifest(pipeline, origins, sources, [.. errors.Listed]) { ErrorCount = errors.Count };
    }

    /// <summary>The format a URL's extension implies, or null.</summary>
    /// <param name="url">The source URL.</param>
    /// <returns>WET for .warc.wet.gz, WARC for .warc.gz, else null.</returns>
    public static CorpusSourceFormat? FormatFromUrl(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        if (path.EndsWith(".warc.wet.gz", StringComparison.OrdinalIgnoreCase))
            return CorpusSourceFormat.Wet;
        if (path.EndsWith(".warc.gz", StringComparison.OrdinalIgnoreCase))
            return CorpusSourceFormat.Warc;
        return null;
    }

    private static CorpusPipelineData ParsePipeline(JsonElement element, CorpusProblems errors)
    {
        var pipeline = new CorpusPipelineData();
        var curlyBracket = false;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("pipeline: must be an object.");
            return pipeline;
        }

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version":
                    pipeline.Version = String(property, "pipeline", errors);
                    break;
                case "language_model_sha256":
                    pipeline.LanguageModelSha256 = String(property, "pipeline", errors).ToLowerInvariant();
                    break;
                case "target_languages":
                    pipeline.TargetLanguages = ParseStrings(property.Value, "pipeline.target_languages", errors);
                    break;
                case "identify_language":
                    pipeline.IdentifyLanguage = Bool(property, "pipeline", errors);
                    break;
                case "clean_lines":
                    pipeline.CleanLines = Bool(property, "pipeline", errors);
                    break;
                case "filter_quality":
                    pipeline.FilterQuality = Bool(property, "pipeline", errors);
                    break;
                case "sign":
                    pipeline.Sign = Bool(property, "pipeline", errors);
                    break;
                case "extract_pdf":
                    pipeline.ExtractPdf = Bool(property, "pipeline", errors);
                    break;
                case "quality":
                    pipeline.Quality = ParseQuality(property.Value, errors);
                    break;
                case "filter_curly_bracket":
                    curlyBracket = Bool(property, "pipeline", errors);
                    break;
                case "minhash":
                    pipeline.MinHash = ParseMinHash(property.Value, errors);
                    break;
                case "output":
                    pipeline.Output = ParseOutput(property, errors);
                    break;
                default:
                    errors.Add($"pipeline: unknown key '{property.Name}'.");
                    break;
            }
        }

        // C4's curly-bracket rule is a rule of the quality stages; it travels with their
        // thresholds (absent, or false, leaves the pipeline exactly as before the switch existed).
        if (curlyBracket)
        {
            pipeline.Quality ??= new CorpusQualityData();
            pipeline.Quality.FilterCurlyBracket = true;
        }

        if (pipeline.LanguageModelSha256.Length > 0 && (pipeline.LanguageModelSha256.Length != 64 || !pipeline.LanguageModelSha256.All(Uri.IsHexDigit)))
            errors.Add("pipeline: 'language_model_sha256' must be 64 hex digits.");

        return pipeline;
    }

    private static CorpusMinHashData ParseMinHash(JsonElement element, CorpusProblems errors)
    {
        var minHash = new CorpusMinHashData();
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("pipeline.minhash: must be an object.");
            return minHash;
        }

        var wellTyped = true;
        foreach (var property in element.EnumerateObject())
        {
            if (!MINHASH_KEYS.Contains(property.Name))
            {
                errors.Add($"pipeline.minhash: unknown key '{property.Name}'.");
                wellTyped = false;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value))
            {
                errors.Add($"pipeline.minhash: '{property.Name}' must be an integer.");
                wellTyped = false;
                continue;
            }

            switch (property.Name)
            {
                case "hashes": minHash.Hashes = value; break;
                case "shingle_words": minHash.ShingleWords = value; break;
                case "bands": minHash.Bands = value; break;
            }
        }

        if (wellTyped && MinHashParameters.Problem(minHash) is { } problem)
            errors.Add($"pipeline.minhash: {problem}");

        return minHash;
    }

    private static CorpusOutputMode ParseOutput(JsonProperty property, CorpusProblems errors)
    {
        if (property.Value.ValueKind == JsonValueKind.String && CorpusOutputModeNames.TryParse(property.Value.GetString(), out var mode))
            return mode;

        errors.Add($"pipeline: 'output' must be \"{CorpusOutputModeNames.FULL}\" or \"{CorpusOutputModeNames.STATS_AND_HASHES}\", not {property.Value.GetRawText()}.");
        return CorpusOutputMode.Full;
    }

    private static CorpusQualityData ParseQuality(JsonElement element, CorpusProblems errors)
    {
        var quality = new CorpusQualityData();
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("pipeline.quality: must be an object.");
            return quality;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!QUALITY_KEYS.Contains(property.Name))
            {
                errors.Add($"pipeline.quality: unknown key '{property.Name}'.");
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var value) || value < 0)
            {
                errors.Add($"pipeline.quality: '{property.Name}' must be a number of 0 or more.");
                continue;
            }

            switch (property.Name)
            {
                case "min_words": quality.MinWords = (int)value; break;
                case "max_words": quality.MaxWords = (int)value; break;
                case "min_mean_word_length": quality.MinMeanWordLength = value; break;
                case "max_mean_word_length": quality.MaxMeanWordLength = value; break;
                case "max_symbol_to_word_ratio": quality.MaxSymbolToWordRatio = value; break;
                case "max_bullet_line_ratio": quality.MaxBulletLineRatio = value; break;
                case "max_ellipsis_line_ratio": quality.MaxEllipsisLineRatio = value; break;
                case "min_alphabetic_word_ratio": quality.MinAlphabeticWordRatio = value; break;
                case "max_duplicate_line_ratio": quality.MaxDuplicateLineRatio = value; break;
                case "max_duplicate_line_char_ratio": quality.MaxDuplicateLineCharRatio = value; break;
            }
        }

        return quality;
    }

    private static void ParseSourceItem(JsonElement item, int index, List<CorpusSourceData> sources, CorpusProblems errors)
    {
        var where = $"sources[{index}]";
        var source = item.ValueKind switch
        {
            JsonValueKind.String => ParseSource(item.GetString() ?? "", null, 0, "", where, errors),
            JsonValueKind.Object => ParseSourceObject(item, where, errors),
            _ => null
        };

        if (source != null)
            sources.Add(source);
        else if (item.ValueKind is not (JsonValueKind.String or JsonValueKind.Object))
            errors.Add($"{where}: must be a URL string or an object.");
    }

    private static CorpusSourceData? ParseSourceObject(JsonElement item, string where, CorpusProblems errors)
    {
        var url = "";
        string? format = null;
        long size = 0;
        var sha = "";

        foreach (var property in item.EnumerateObject())
        {
            switch (property.Name)
            {
                case "url":
                    url = String(property, where, errors);
                    break;
                case "format":
                    format = String(property, where, errors);
                    break;
                case "size":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out size) || size < 0)
                        errors.Add($"{where}: 'size' must be an integer of 0 or more.");
                    break;
                case "sha256":
                    sha = String(property, where, errors).ToLowerInvariant();
                    break;
                default:
                    errors.Add($"{where}: unknown key '{property.Name}'.");
                    break;
            }
        }

        if (sha.Length > 0 && (sha.Length != 64 || !sha.All(Uri.IsHexDigit)))
            errors.Add($"{where}: 'sha256' must be 64 hex digits.");

        return ParseSource(url, format, size, sha, where, errors);
    }

    private static CorpusSourceData? ParseSource(string url, string? format, long size, string sha, string where, CorpusProblems errors)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add($"{where}: 'url' is required.");
            return null;
        }

        CorpusSourceFormat? resolved;
        if (format == null)
        {
            resolved = FormatFromUrl(url);
            if (resolved == null)
            {
                errors.Add($"{where}: cannot tell the format of '{url}' from its extension; give 'format' as 'wet' or 'warc'.");
                return null;
            }
        }
        else if (string.Equals(format, "wet", StringComparison.OrdinalIgnoreCase))
            resolved = CorpusSourceFormat.Wet;
        else if (string.Equals(format, "warc", StringComparison.OrdinalIgnoreCase))
            resolved = CorpusSourceFormat.Warc;
        else
        {
            errors.Add($"{where}: 'format' must be 'wet' or 'warc', not '{format}'.");
            return null;
        }

        return new CorpusSourceData { Url = url.Trim(), Format = resolved.Value, SizeBytes = size, Sha256 = sha };
    }

    private static List<string> ParseStrings(JsonElement element, string where, CorpusProblems errors)
    {
        var values = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{where}: must be an array of strings.");
            return values;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                errors.Add($"{where}: every entry must be a non-empty string.");
                continue;
            }

            values.Add(item.GetString()!.Trim());
        }

        return values;
    }

    private static string String(JsonProperty property, string where, CorpusProblems errors)
    {
        if (property.Value.ValueKind == JsonValueKind.String)
            return property.Value.GetString() ?? "";

        errors.Add($"{where}: '{property.Name}' must be a string.");
        return "";
    }

    private static bool Bool(JsonProperty property, string where, CorpusProblems errors)
    {
        if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return property.Value.GetBoolean();

        errors.Add($"{where}: '{property.Name}' must be true or false.");
        return true;
    }

    #endregion
}
