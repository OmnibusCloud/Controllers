using System.Text.Json;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The capabilities document: what an application can know before a run. The controller's
/// version and stage set, the parsers, the language-pack formats and the reference pack,
/// where a machine fetches from, the defaults and the ceilings, the stage parameters a job may
/// set with their defaults, the products, the measured storage ratios with the deployment's
/// headroom, and the pool as totals. A JSON document like every other output, so an
/// application reads it without the controller's types. Schema <c>corpus-capabilities/1</c>.
/// </summary>
public static class CorpusCapabilitiesWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "corpus-capabilities/1";

    #endregion

    #region Functions

    /// <summary>Serializes the document as UTF-8 JSON.</summary>
    /// <param name="diagnostics">The host module's diagnostics.</param>
    /// <param name="pool">The pool's totals, or null when the host cannot say.</param>
    /// <param name="limit">The deployment's storage limit (<see cref="CorpusStorageLimit.NONE"/> when nothing is stated).</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(CorpusRuntimeDiagnosticsData diagnostics, CorpusPoolSummary? pool, CorpusStorageLimit limit)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", diagnostics.ControllerVersion);
            writer.WriteString("benchmark_unit", CorpusBenchmark.UNIT);

            WriteStages(writer, diagnostics);
            WriteParsers(writer, diagnostics);
            WriteLanguageModel(writer);
            WriteStrings(writer, "formats", Enum.GetNames<CorpusSourceFormat>().Select(name => name.ToLowerInvariant()));
            WriteOrigins(writer, diagnostics);
            WriteDefaults(writer);
            WriteCeilings(writer);
            WritePipeline(writer);
            WriteSignatures(writer);
            WriteProducts(writer);
            WriteStorage(writer, limit);
            WritePool(writer, pool);

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The manifest's quality keys and the rule each sets, in the manifest's order.</summary>
    /// <returns>Key and reader pairs.</returns>
    public static IReadOnlyList<(string Key, Func<QualityRules, double> Read)> QualityKeys()
    {
        return
        [
            ("min_words", rules => rules.MinWords),
            ("max_words", rules => rules.MaxWords),
            ("min_mean_word_length", rules => rules.MinMeanWordLength),
            ("max_mean_word_length", rules => rules.MaxMeanWordLength),
            ("max_symbol_to_word_ratio", rules => rules.MaxSymbolToWordRatio),
            ("max_bullet_line_ratio", rules => rules.MaxBulletLineRatio),
            ("max_ellipsis_line_ratio", rules => rules.MaxEllipsisLineRatio),
            ("min_alphabetic_word_ratio", rules => rules.MinAlphabeticWordRatio),
            ("max_duplicate_line_ratio", rules => rules.MaxDuplicateLineRatio),
            ("max_duplicate_line_char_ratio", rules => rules.MaxDuplicateLineCharRatio)
        ];
    }

    private static void WriteStages(Utf8JsonWriter writer, CorpusRuntimeDiagnosticsData diagnostics)
    {
        writer.WriteStartObject("stages");
        WriteStrings(writer, "order", diagnostics.Stages);
        writer.WriteString("sha256", diagnostics.StageSetSha256);
        writer.WriteBoolean("verified", diagnostics.StagesVerified);
        writer.WriteString("language_format", CorpusStageSet.LANGUAGE_FORMAT);
        writer.WriteEndObject();
    }

    private static void WriteParsers(Utf8JsonWriter writer, CorpusRuntimeDiagnosticsData diagnostics)
    {
        writer.WriteStartObject("parsers");
        writer.WriteString("warc", $"own streamed reader, controller {diagnostics.ControllerVersion}");
        writer.WriteString("html", diagnostics.HtmlParser);
        writer.WriteString("compressor", diagnostics.Compressor);
        writer.WriteEndObject();
    }

    private static void WriteLanguageModel(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("language_model");
        writer.WriteString("format", CorpusReferenceLanguageModel.FORMAT);
        WriteStrings(writer, "formats", LanguageModelFormat.READ);
        writer.WriteString("method", CorpusReferenceLanguageModel.METHOD);
        writer.WriteString("reference_name", CorpusReferenceLanguageModel.NAME);
        writer.WriteString("reference_sha256", CorpusReferenceLanguageModel.SHA256);
        writer.WriteString("reference_trained_from", CorpusReferenceLanguageModel.TRAINED_FROM);
        WriteStrings(writer, "reference_languages", CorpusReferenceLanguageModel.LANGUAGES);
        WriteStrings(writer, "reference_known_languages", CorpusReferenceLanguageModel.KNOWN_LANGUAGES);
        writer.WriteString("witlid1_reference_sha256", CorpusReferenceLanguageModel.WITLID1_SHA256);
        writer.WriteString("unsupported_drop", ShardProcessor.DROP_LANGUAGE_UNSUPPORTED);
        writer.WriteString("unknown_drop", ShardProcessor.DROP_LANGUAGE_UNKNOWN);
        writer.WriteEndObject();
    }

    private static void WriteOrigins(Utf8JsonWriter writer, CorpusRuntimeDiagnosticsData diagnostics)
    {
        writer.WriteStartObject("origins");
        WriteStrings(writer, "public", diagnostics.PublicOrigins);
        writer.WriteString("machine_variable", ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE);
        writer.WriteNumber("max_redirects", ShardHttp.MAX_REDIRECTS);
        writer.WriteEndObject();
    }

    private static void WriteDefaults(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("defaults");
        writer.WriteNumber("max_shard_bytes", CorpusTaskPlanner.MAX_SHARD_BYTES);
        writer.WriteNumber("sampling_rate", 0);
        writer.WriteNumber("dedup_threshold", CorpusDeduplicator.DEFAULT_THRESHOLD);
        writer.WriteNumber("downloads_per_node", ShardFetchPolicy.Default.MaxConcurrent);
        writer.WriteNumber("memory_per_shard_bytes", ShardDegree.MEMORY_PER_SHARD);
        writer.WriteEndObject();
    }

    private static void WriteCeilings(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("ceilings");
        writer.WriteNumber("max_shard_bytes", CorpusTaskPlanner.MAX_SHARD_BYTES);
        writer.WriteNumber("sampling_rate", 1);
        writer.WriteNumber("record_bytes", WarcReader.DEFAULT_MAX_RECORD_BYTES);
        writer.WriteNumber("html_chars", HtmlTextExtractor.MAX_HTML_CHARS);
        writer.WriteNumber("html_nesting", HtmlTextExtractor.MAX_NESTING);
        writer.WriteNumber("html_tags", HtmlTextExtractor.MAX_TAGS);
        writer.WriteNumber("size_probes", ShardSizeProbe.MAX_PROBES);
        writer.WriteNumber("dedup_documents", CorpusDeduplicator.MAX_DOCUMENTS);
        writer.WriteNumber("dedup_clusters_listed", CorpusDedupWriter.MAX_CLUSTERS_LISTED);
        writer.WriteEndObject();
    }

    private static void WritePipeline(Utf8JsonWriter writer)
    {
        var defaults = new CorpusPipelineData();

        writer.WriteStartObject("pipeline");

        writer.WriteStartObject("switches");
        writer.WriteBoolean("identify_language", defaults.IdentifyLanguage);
        writer.WriteBoolean("clean_lines", defaults.CleanLines);
        writer.WriteBoolean("filter_quality", defaults.FilterQuality);
        writer.WriteBoolean("sign", defaults.Sign);
        writer.WriteBoolean("filter_curly_bracket", new CorpusQualityData().FilterCurlyBracket);
        writer.WriteEndObject();

        writer.WriteStartObject("quality");
        foreach (var (key, read) in QualityKeys())
        {
            writer.WriteStartObject(key);
            WriteLimit(writer, "spaced", read(QualityRules.Spaced));
            WriteLimit(writer, "unspaced", read(QualityRules.Unspaced));
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WriteStartObject("quality_fixed");
        WriteNumbers(writer, "top_ngram_chars", QualityRules.Spaced.MaxTopNgramCharRatio);
        WriteNumbers(writer, "duplicate_ngram_chars", QualityRules.Spaced.MaxDuplicateNgramCharRatio);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteSignatures(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("signatures");
        writer.WriteNumber("hashes", MinHashSigner.HASHES);
        writer.WriteNumber("shingle_words", MinHashSigner.SHINGLE_WORDS);
        writer.WriteNumber("bytes_per_document", CorpusStorageRatios.SIGNATURE_BYTES_PER_DOCUMENT);
        writer.WriteNumber("bands", CorpusDeduplicator.DEFAULT_BANDS);
        writer.WriteNumber("rows_per_band", MinHashSigner.HASHES / CorpusDeduplicator.DEFAULT_BANDS);
        writer.WriteEndObject();
    }

    private static void WriteProducts(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("products");
        WriteProduct(writer, "text", "text.jsonl.zst", "shard", "");
        WriteProduct(writer, "signatures", "signatures.bin", "shard", "");
        WriteProduct(writer, "report", "corpus-report.json", "job", CorpusReportWriter.SCHEMA);
        WriteProduct(writer, "integrity", "corpus-integrity.json", "job", CorpusIntegrityWriter.SCHEMA);
        WriteProduct(writer, "dedup", "corpus-dedup.json", "dedup", CorpusDedupWriter.SCHEMA);
        writer.WriteEndArray();
    }

    private static void WriteProduct(Utf8JsonWriter writer, string name, string file, string per, string schema)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("file", file);
        writer.WriteString("per", per);
        writer.WriteString("schema", schema);
        writer.WriteEndObject();
    }

    private static void WriteStorage(Utf8JsonWriter writer, CorpusStorageLimit limit)
    {
        writer.WriteStartObject("storage");

        writer.WriteStartArray("ratios");
        foreach (var ratio in CorpusStorageRatios.TABLE)
        {
            writer.WriteStartObject();
            writer.WriteString("format", ratio.Format.ToString().ToLowerInvariant());
            writer.WriteBoolean("clean_lines", ratio.CleanLines);
            writer.WriteBoolean("filter_quality", ratio.FilterQuality);
            writer.WriteNumber("text_per_byte", ratio.TextPerByte);
            writer.WriteNumber("documents_per_mib", ratio.DocumentsPerMib);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteNumber("margin", CorpusStorageRatios.MARGIN);
        if (limit.IsStated)
            writer.WriteNumber("headroom_bytes", limit.HeadroomBytes);
        if (limit.RetentionDays > 0)
            writer.WriteNumber("retention_days", limit.RetentionDays);

        writer.WriteEndObject();
    }

    private static void WritePool(Utf8JsonWriter writer, CorpusPoolSummary? pool)
    {
        if (pool == null)
            return;

        writer.WriteStartObject("pool");
        writer.WriteNumber("nodes", pool.Nodes);
        writer.WriteNumber("measured", pool.Measured);
        if (pool.Workers is { } workers)
            writer.WriteNumber("workers", workers);
        writer.WriteNumber("rate", Math.Round(pool.Rate, 3));
        writer.WriteEndObject();
    }

    private static void WriteLimit(Utf8JsonWriter writer, string name, double value)
    {
        // A rule that a script's base turns off carries no number: there is no limit to show.
        if (double.IsInfinity(value) || value >= double.MaxValue)
            writer.WriteNull(name);
        else
            writer.WriteNumber(name, value);
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteNumbers(Utf8JsonWriter writer, string name, IEnumerable<double> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }

    #endregion
}
