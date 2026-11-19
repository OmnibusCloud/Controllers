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
/// shard says how many results it rests on (<c>executions</c>: 1 is one machine's word). Beside
/// the summary stand the sampling the run asked for with what came back of it, in a word and in a
/// sentence, and how each round's fan-out came back (<c>sampling</c>, <c>rounds</c>:
/// <see cref="CorpusFanOutWriter"/>): a run whose checking did not happen says so. No
/// document says which machine ran what or what that machine is (its platform is named,
/// <c>runtime_target</c>; its cores and memory are not), and no ticket is written. Schema
/// <c>corpus-report/5</c>: the shape of <c>/4</c> with <c>sampling</c> and <c>rounds</c>;
/// <c>/3</c> also said how many shards the machine of each result ran at once (<c>degree</c>).
/// </summary>
public static class CorpusReportWriter
{
    #region Constants

    /// <summary>The report schema name.</summary>
    public const string SCHEMA = "corpus-report/5";

    /// <summary>The earlier report schemas Corpus.Dedup still reads: <c>/4</c> did not state the sampling asked for or how the rounds came back, <c>/3</c> said the degree of the node that ran each shard, <c>/2</c> chose between executions by what they said, <c>/1</c> also named the nodes.</summary>
    public static readonly IReadOnlyList<string> PREVIOUS_SCHEMAS = ["corpus-report/4", "corpus-report/3", "corpus-report/2", "corpus-report/1"];

    /// <summary>The bytes the writer holds before it hands them to the stream.</summary>
    private const int FLUSH_BYTES = 1 << 16;

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
        using var buffer = new MemoryStream();
        Write(ledger, buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Writes the report to a stream, a shard at a time: the run is read twice (once for the
    /// summary, which stands first in the document, once for the shards), and no more of it is
    /// held than the ledger holds, so a report of any length is written in the memory of a short one.
    /// </summary>
    /// <param name="ledger">The server's account of the run.</param>
    /// <param name="stream">Receives the UTF-8 JSON.</param>
    public static void Write(CorpusLedger ledger, Stream stream)
    {
        var drops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var languages = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var unsupported = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        long recordsIn = 0, extracted = 0, documentsOut = 0, fetched = 0, produced = 0;
        PdfTotals? pdf = null;
        var output = CorpusOutputMode.Full;
        CorpusMinHashData? minHash = null;
        var oneMinHash = true;
        var shards = 0;

        foreach (var shard in ledger.Shards)
        {
            shards++;
            var verdict = Verdict(shard);
            verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
            if (shard.Task.Pipeline?.Output == CorpusOutputMode.StatsAndHashes)
                output = CorpusOutputMode.StatsAndHashes;

            if (shard.Accepted?.MinHash is { } signed)
            {
                minHash ??= signed;
                oneMinHash &= signed.Is(minHash);
            }

            if (shard.Accepted?.Stats is not { } stats)
                continue;

            // Sums of what the nodes said, over a run of any length: in 64 bits, and held at the
            // largest number rather than wrapped past it.
            recordsIn = Sum(recordsIn, stats.RecordsIn);
            extracted = Sum(extracted, stats.DocumentsExtracted);
            documentsOut = Sum(documentsOut, stats.DocumentsOut);
            fetched = Sum(fetched, stats.BytesFetched);
            produced = Sum(produced, stats.BytesProduced);
            Add(drops, stats.Drops);
            Add(languages, stats.Languages);
            Add(unsupported, stats.UnsupportedLanguages);
            if (stats.Pdf is { } shardPdf)
                (pdf ??= new PdfTotals()).Add(shardPdf);
        }

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);
        writer.WriteString("controller", ledger.ControllerVersion);

        writer.WriteStartObject("summary");
        writer.WriteNumber("tasks", shards);
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
        if (pdf != null)
            WritePdf(writer, pdf.In, pdf.Truncated, pdf.Extracted, pdf.Pages, pdf.Documents, pdf.PagesCut, pdf.RunningLines, pdf.Fuel);
        WriteFunnel(writer, recordsIn, extracted, documentsOut, drops);
        writer.WriteString("output", CorpusOutputModeNames.Name(output));
        if (minHash != null && oneMinHash)
            WriteMinHash(writer, minHash);
        writer.WriteEndObject();

        // What the run asked for in second executions, and how each round came back.
        CorpusFanOutWriter.Write(writer, ledger);

        writer.WriteStartArray("shards");
        foreach (var shard in ledger.Shards)
        {
            WriteShard(writer, shard);

            // What is written goes to the stream as it grows, not at the end.
            if (writer.BytesPending > FLUSH_BYTES)
                writer.Flush();
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
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
            if (stats.Pdf is { } pdf)
                WritePdf(writer, pdf.In, pdf.Truncated, pdf.Extracted, pdf.Pages, pdf.Documents, pdf.PagesCut, pdf.RunningLines, pdf.Fuel);
            writer.WriteEndObject();
        }
        else
            writer.WriteNull("stats");

        writer.WriteEndObject();
    }

    /// <summary>The PDF stage's counts, flat beside the others: a shard's own, or their sums over the run.</summary>
    private static void WritePdf(Utf8JsonWriter writer, long pdfIn, long truncated, long extracted, long pages, long documents, long pagesCut, long runningLines, long fuel)
    {
        writer.WriteNumber("pdf_in", pdfIn);
        writer.WriteNumber("pdf_truncated", truncated);
        writer.WriteNumber("pdf_extracted", extracted);
        writer.WriteNumber("pdf_pages", pages);
        writer.WriteNumber("pdf_documents", documents);
        writer.WriteNumber("pdf_pages_cut", pagesCut);
        writer.WriteNumber("pdf_running_lines", runningLines);
        writer.WriteNumber("pdf_fuel", fuel);
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

    /// <summary>A sum that stops at the largest number: the values are counts and bytes, never below zero.</summary>
    private static long Sum(long sum, long value)
    {
        return value > long.MaxValue - sum ? long.MaxValue : sum + value;
    }

    private static void Add(SortedDictionary<string, long> sums, IReadOnlyList<CorpusCountData> counts)
    {
        foreach (var count in counts)
            sums[count.Name] = Sum(sums.GetValueOrDefault(count.Name), count.Count);
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

    #region Types

    /// <summary>
    /// The PDF stage's counts over a run. A shard states them in 32 bits and a run has up to a
    /// million shards, so the sums are kept in 64.
    /// </summary>
    private sealed class PdfTotals
    {
        public void Add(CorpusPdfStatsData shard)
        {
            In = Sum(In, shard.In);
            Truncated = Sum(Truncated, shard.Truncated);
            Extracted = Sum(Extracted, shard.Extracted);
            Pages = Sum(Pages, shard.Pages);
            Documents = Sum(Documents, shard.Documents);
            PagesCut = Sum(PagesCut, shard.PagesCut);
            RunningLines = Sum(RunningLines, shard.RunningLines);
            Fuel = Sum(Fuel, shard.Fuel);
        }

        public long In { get; private set; }

        public long Truncated { get; private set; }

        public long Extracted { get; private set; }

        public long Pages { get; private set; }

        public long Documents { get; private set; }

        public long PagesCut { get; private set; }

        public long RunningLines { get; private set; }

        public long Fuel { get; private set; }
    }

    #endregion
}
