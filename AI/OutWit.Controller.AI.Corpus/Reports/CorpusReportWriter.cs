using System.Text.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// The job report: every shard of the job in task order, with how it ended — the verdict of its
/// accepted result with its blobs and content hash, <c>Disputed</c> (its copies disagreed and no
/// majority of three machines settled it), or <c>NoResult</c> with a fixed reason (no machine
/// returned a result that could be accepted) — and the aggregate over the accepted results:
/// documents in and out, bytes moved, the funnel (records, documents extracted, and what is left
/// after the language, line and quality stages), the drop and language histograms, what the PDF
/// stage did (the <c>pdf_*</c> counts, present only where a shard met a PDF), what the run kept on
/// the server (<c>output</c>) and the MinHash parameters of its signatures. The shards, their
/// indexes and their URLs are the server's own task list: a shard is in the report whether or not
/// anything came back for it, and nothing a node returns can put another URL there. Each accepted
/// shard says how many results it rests on (<c>executions</c>: 1 is one machine's word). No
/// document says which machine ran what, and no ticket is written. Schema <c>corpus-report/3</c>.
/// </summary>
public static class CorpusReportWriter
{
    #region Constants

    /// <summary>The report schema name.</summary>
    public const string SCHEMA = "corpus-report/3";

    /// <summary>The earlier report schemas Corpus.Dedup still reads: <c>/2</c> chose between executions by what they said, <c>/1</c> also named the nodes.</summary>
    public static readonly IReadOnlyList<string> PREVIOUS_SCHEMAS = ["corpus-report/2", "corpus-report/1"];

    /// <summary>The report's word for a shard whose copies disagreed and stayed unsettled.</summary>
    public const string VERDICT_DISPUTED = "Disputed";

    /// <summary>The report's word for a shard no machine returned an acceptable result for.</summary>
    public const string VERDICT_NO_RESULT = "NoResult";

    #endregion

    #region Functions

    /// <summary>Writes the report: one entry per shard of the job.</summary>
    /// <param name="ledger">The server's account of the run.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] Write(CorpusLedger ledger)
    {
        var drops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var languages = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var unsupported = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        long recordsIn = 0, extracted = 0, documentsOut = 0, fetched = 0, produced = 0;
        CorpusPdfStatsData? pdf = null;
        var accepted = ledger.Shards.Where(shard => shard.Accepted != null).Select(shard => shard.Accepted!).ToList();
        var output = ledger.Shards.Any(shard => shard.Task.Pipeline?.Output == CorpusOutputMode.StatsAndHashes) ? CorpusOutputMode.StatsAndHashes : CorpusOutputMode.Full;
        var minHashes = accepted.Where(result => result.MinHash != null).Select(result => result.MinHash!).ToList();

        foreach (var shard in ledger.Shards)
        {
            var verdict = Verdict(shard);
            verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
            if (shard.Accepted?.Stats is not { } stats)
                continue;

            recordsIn += stats.RecordsIn;
            extracted += stats.DocumentsExtracted;
            documentsOut += stats.DocumentsOut;
            fetched += stats.BytesFetched;
            produced += stats.BytesProduced;
            Add(drops, stats.Drops);
            Add(languages, stats.Languages);
            Add(unsupported, stats.UnsupportedLanguages);
            if (stats.Pdf is { } shardPdf)
                pdf = Add(pdf ?? new CorpusPdfStatsData(), shardPdf);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ledger.ControllerVersion);

            writer.WriteStartObject("summary");
            writer.WriteNumber("tasks", ledger.Shards.Count);
            writer.WriteStartObject("verdicts");
            foreach (var pair in verdicts)
                writer.WriteNumber(pair.Key, pair.Value);
            writer.WriteEndObject();
            writer.WriteNumber("single_execution", ledger.SingleExecutions);
            writer.WriteNumber("rejected_results", ledger.RejectedResults);
            writer.WriteNumber("repairs", ledger.Repairs);
            writer.WriteNumber("disputes", ledger.Disputes);
            writer.WriteNumber("disputes_resolved", ledger.DisputesResolved);
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
            foreach (var shard in ledger.Shards)
                WriteShard(writer, shard);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>The report's word for how a shard ended: its accepted result's verdict, or one of the two outcomes without a result.</summary>
    /// <param name="shard">The shard.</param>
    /// <returns>A verdict name, <see cref="VERDICT_DISPUTED"/> or <see cref="VERDICT_NO_RESULT"/>.</returns>
    public static string Verdict(CorpusLedgerShard shard)
    {
        return shard.Outcome switch
        {
            CorpusShardOutcome.Accepted => shard.Accepted!.Verdict.ToString(),
            CorpusShardOutcome.Disputed => VERDICT_DISPUTED,
            _ => VERDICT_NO_RESULT
        };
    }

    private static void WriteShard(Utf8JsonWriter writer, CorpusLedgerShard shard)
    {
        var result = shard.Accepted;

        writer.WriteStartObject();
        writer.WriteNumber("index", shard.Index);
        writer.WriteString("url", shard.Url);
        writer.WriteString("verdict", Verdict(shard));
        if (shard.Outcome == CorpusShardOutcome.NoResult)
            writer.WriteString("reason", shard.Reason);
        writer.WriteNumber("executions", shard.Copies);
        if (shard.Faults.Count > 0)
        {
            writer.WriteStartArray("faults");
            foreach (var fault in shard.Faults)
                writer.WriteStringValue(fault);
            writer.WriteEndArray();
        }

        writer.WriteString("text_blob", result == null || result.TextBlobId == Guid.Empty ? "" : result.TextBlobId.ToString("D"));
        writer.WriteString("signatures_blob", result == null || result.SignaturesBlobId == Guid.Empty ? "" : result.SignaturesBlobId.ToString("D"));
        writer.WriteString("content_sha256", result?.ContentSha256 ?? "");
        writer.WriteString("error", result?.Error ?? "");
        writer.WriteString("controller", result?.ControllerVersion ?? "");
        writer.WriteString("runtime_target", result?.RuntimeTarget ?? "");
        writer.WriteNumber("degree", result?.Degree ?? 0);
        if (result?.MinHash != null)
            WriteMinHash(writer, result.MinHash);
        writer.WriteString("signatures_sha256", result?.SignaturesSha256 ?? "");

        if (result?.Stats is { } stats)
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
            WriteCounts(writer, "drops", Sum(stats.Drops));
            WriteCounts(writer, "languages", Sum(stats.Languages));
            WriteCounts(writer, "unsupported_languages", Sum(stats.UnsupportedLanguages));
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

    private static void Add(SortedDictionary<string, long> sums, IReadOnlyList<CorpusCountData> counts)
    {
        foreach (var count in counts)
            sums[count.Name] = sums.GetValueOrDefault(count.Name) + count.Count;
    }

    private static SortedDictionary<string, long> Sum(IReadOnlyList<CorpusCountData> counts)
    {
        var sums = new SortedDictionary<string, long>(StringComparer.Ordinal);
        Add(sums, counts);
        return sums;
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
