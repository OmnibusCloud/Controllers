using System.Text.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// The job report: every shard's verdict, blobs and content hash in task order, and the
/// aggregate — documents in and out, bytes moved, the funnel (records, documents extracted, and
/// what is left after the language, line and quality stages), the drop and language histograms
/// summed over the fleet, what the PDF stage did (the <c>pdf_*</c> counts, present only where a
/// shard met a PDF), what the run kept on the server (<c>output</c>) and the MinHash parameters of
/// its signatures. No document says which machine ran what. Schema <c>corpus-report/2</c>: every
/// field added since its first version is optional to a reader.
/// </summary>
public static class CorpusReportWriter
{
    #region Constants

    /// <summary>The report schema name.</summary>
    public const string SCHEMA = "corpus-report/2";

    /// <summary>The earlier report schema, which named the nodes; Corpus.Dedup still reads it.</summary>
    public const string PREVIOUS_SCHEMA = "corpus-report/1";

    #endregion

    #region Functions

    /// <summary>Writes the report: one entry per shard, however many times it was executed.</summary>
    /// <param name="results">The executions, any order.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] Write(IReadOnlyList<CorpusResultData> results)
    {
        var ordered = CorpusExecutions.Canonical(results);
        var drops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var languages = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var unsupported = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        long recordsIn = 0, extracted = 0, documentsOut = 0, fetched = 0, produced = 0;
        CorpusPdfStatsData? pdf = null;
        var output = ordered.Any(result => result.Output == CorpusOutputMode.StatsAndHashes) ? CorpusOutputMode.StatsAndHashes : CorpusOutputMode.Full;
        var minHashes = ordered.Where(result => result.MinHash != null).Select(result => result.MinHash!).ToList();
        foreach (var result in ordered)
        {
            verdicts[result.Verdict.ToString()] = verdicts.GetValueOrDefault(result.Verdict.ToString()) + 1;
            if (result.Stats == null)
                continue;

            recordsIn += result.Stats.RecordsIn;
            extracted += result.Stats.DocumentsExtracted;
            documentsOut += result.Stats.DocumentsOut;
            fetched += result.Stats.BytesFetched;
            produced += result.Stats.BytesProduced;
            foreach (var drop in result.Stats.Drops)
                drops[drop.Name] = drops.GetValueOrDefault(drop.Name) + drop.Count;
            foreach (var language in result.Stats.Languages)
                languages[language.Name] = languages.GetValueOrDefault(language.Name) + language.Count;
            foreach (var language in result.Stats.UnsupportedLanguages)
                unsupported[language.Name] = unsupported.GetValueOrDefault(language.Name) + language.Count;
            if (result.Stats.Pdf is { } shardPdf)
                pdf = Add(pdf ?? new CorpusPdfStatsData(), shardPdf);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ControllerBuildInfo.VERSION);

            writer.WriteStartObject("summary");
            writer.WriteNumber("tasks", ordered.Count);
            writer.WriteStartObject("verdicts");
            foreach (var pair in verdicts)
                writer.WriteNumber(pair.Key, pair.Value);
            writer.WriteEndObject();
            writer.WriteNumber("records_in", recordsIn);
            writer.WriteNumber("documents_extracted", extracted);
            writer.WriteNumber("documents_out", documentsOut);
            writer.WriteNumber("bytes_fetched", fetched);
            writer.WriteNumber("bytes_produced", produced);
            WriteCounts(writer, "drops", drops);
            WriteCounts(writer, "languages", languages);
            WriteCounts(writer, "unsupported_languages", unsupported);
            WritePdf(writer, pdf);
            WriteFunnel(writer, recordsIn, extracted, documentsOut, drops);
            writer.WriteString("output", CorpusOutputModeNames.Name(output));
            if (minHashes.Count > 0 && minHashes.All(minHash => minHash.Is(minHashes[0])))
                WriteMinHash(writer, minHashes[0]);
            writer.WriteEndObject();

            writer.WriteStartArray("shards");
            foreach (var result in ordered)
                WriteShard(writer, result);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteShard(Utf8JsonWriter writer, CorpusResultData result)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", result.TaskIndex);
        writer.WriteString("url", result.SourceUrl);
        writer.WriteString("verdict", result.Verdict.ToString());
        writer.WriteString("text_blob", result.TextBlobId == Guid.Empty ? "" : result.TextBlobId.ToString("D"));
        writer.WriteString("signatures_blob", result.SignaturesBlobId == Guid.Empty ? "" : result.SignaturesBlobId.ToString("D"));
        writer.WriteString("content_sha256", result.ContentSha256);
        writer.WriteString("error", result.Error);
        writer.WriteString("controller", result.ControllerVersion);
        writer.WriteString("runtime_target", result.RuntimeTarget);
        writer.WriteNumber("degree", result.Degree);
        if (result.MinHash != null)
            WriteMinHash(writer, result.MinHash);

        if (result.Stats is { } stats)
        {
            writer.WriteStartObject("stats");
            writer.WriteNumber("records_in", stats.RecordsIn);
            writer.WriteNumber("documents_extracted", stats.DocumentsExtracted);
            writer.WriteNumber("documents_out", stats.DocumentsOut);
            writer.WriteNumber("bytes_fetched", stats.BytesFetched);
            writer.WriteNumber("bytes_produced", stats.BytesProduced);
            writer.WriteNumber("fetch_ms", stats.FetchMs);
            writer.WriteNumber("process_ms", stats.ProcessMs);
            writer.WriteNumber("wait_ms", stats.WaitMs);
            WriteCounts(writer, "drops", stats.Drops.ToDictionary(count => count.Name, count => (long)count.Count, StringComparer.Ordinal));
            WriteCounts(writer, "languages", stats.Languages.ToDictionary(count => count.Name, count => (long)count.Count, StringComparer.Ordinal));
            WriteCounts(writer, "unsupported_languages", stats.UnsupportedLanguages.ToDictionary(count => count.Name, count => (long)count.Count, StringComparer.Ordinal));
            WritePdf(writer, stats.Pdf);
            writer.WriteEndObject();
        }
        else
            writer.WriteNull("stats");

        writer.WriteEndObject();
    }

    /// <summary>The PDF stage's counts, flat beside the others; nothing when the stage met no PDF.</summary>
    private static void WritePdf(Utf8JsonWriter writer, CorpusPdfStatsData? pdf)
    {
        if (pdf == null)
            return;

        writer.WriteNumber("pdf_in", pdf.In);
        writer.WriteNumber("pdf_truncated", pdf.Truncated);
        writer.WriteNumber("pdf_extracted", pdf.Extracted);
        writer.WriteNumber("pdf_pages", pdf.Pages);
        writer.WriteNumber("pdf_documents", pdf.Documents);
        writer.WriteNumber("pdf_pages_cut", pdf.PagesCut);
        writer.WriteNumber("pdf_running_lines", pdf.RunningLines);
        writer.WriteNumber("pdf_fuel", pdf.Fuel);
    }

    /// <summary>
    /// The run's funnel: the records read, the documents extracted, and what is left after the
    /// language stage, the line stage and the quality filter (the documents out), with the drops
    /// of each stage. The extraction drops count records and PDFs that gave no document, so they
    /// are beside the funnel, not inside it: a WARC shard's requests and metadata records are read
    /// and are no documents.
    /// </summary>
    private static void WriteFunnel(Utf8JsonWriter writer, long records, long extracted, long documentsOut, IReadOnlyDictionary<string, long> drops)
    {
        var dropped = ShardDropRules.STAGES.ToDictionary(stage => stage, _ => 0L, StringComparer.Ordinal);
        foreach (var (rule, count) in drops)
            dropped[ShardDropRules.StageOf(rule)] += count;

        var language = extracted - dropped[ShardDropRules.STAGE_LANGUAGE];
        var lines = language - dropped[ShardDropRules.STAGE_LINES];
        var quality = lines - dropped[ShardDropRules.STAGE_QUALITY];

        writer.WriteStartObject("funnel");
        writer.WriteNumber("records", records);
        writer.WriteNumber("extracted", extracted);
        writer.WriteNumber("language", language);
        writer.WriteNumber("lines", lines);
        writer.WriteNumber("quality", quality);
        writer.WriteNumber("out", documentsOut);
        writer.WriteStartObject("dropped");
        foreach (var stage in ShardDropRules.STAGES)
            writer.WriteNumber(stage, dropped[stage]);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteMinHash(Utf8JsonWriter writer, CorpusMinHashData minHash)
    {
        writer.WriteStartObject("minhash");
        writer.WriteNumber("hashes", minHash.Hashes);
        writer.WriteNumber("shingle_words", minHash.ShingleWords);
        writer.WriteNumber("bands", minHash.Bands);
        writer.WriteEndObject();
    }

    private static CorpusPdfStatsData Add(CorpusPdfStatsData sum, CorpusPdfStatsData shard)
    {
        sum.In += shard.In;
        sum.Truncated += shard.Truncated;
        sum.Extracted += shard.Extracted;
        sum.Pages += shard.Pages;
        sum.Documents += shard.Documents;
        sum.PagesCut += shard.PagesCut;
        sum.RunningLines += shard.RunningLines;
        sum.Fuel += shard.Fuel;
        return sum;
    }

    private static void WriteCounts(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, long> counts)
    {
        writer.WriteStartObject(name);
        foreach (var pair in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteNumber(pair.Key, pair.Value);
        writer.WriteEndObject();
    }

    #endregion
}
