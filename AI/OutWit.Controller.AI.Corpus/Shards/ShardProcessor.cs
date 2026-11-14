using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The pipeline over one shard: records stream in, each becomes text (WET as is, WARC through
/// the HTML extractor, a WARC response that is a PDF through the PDF stage's sandbox into one
/// document per page range), passes or fails the language and quality stages, and the survivors
/// leave as one JSON line each — <c>{"url","lang","text"}</c> — hashed uncompressed, written
/// zstd-compressed, with a MinHash signature per document in a side blob. A pure function of
/// the shard bytes and the pipeline: two nodes produce the same hash or one of them is wrong.
/// </summary>
public sealed class ShardProcessor
{
    #region Constants

    private const string RECORD_CONVERSION = "conversion";
    private const string RECORD_RESPONSE = "response";

    /// <summary>Drop rules the stages before the quality filter report.</summary>
    public const string DROP_HTTP_UNPARSABLE = "http_unparsable";
    public const string DROP_HTTP_STATUS = "http_status";
    public const string DROP_NOT_HTML = "not_html";
    public const string DROP_HTML_REFUSED = "html_refused";
    public const string DROP_EMPTY_TEXT = "empty_text";
    public const string DROP_LANGUAGE_UNKNOWN = "language_unknown";
    public const string DROP_LANGUAGE_UNSUPPORTED = "language_unsupported";
    public const string DROP_LANGUAGE_NOT_TARGET = "language_not_target";
    public const string DROP_NO_PROSE_LINES = "no_prose_lines";
    public const string DROP_CURLY_BRACKET = "curly_bracket";

    private static readonly JsonWriterOptions JSON_OPTIONS = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false, SkipValidation = true };

    #endregion

    #region Fields

    private readonly CorpusPipelineData m_pipeline;
    private readonly LanguageIdentifier? m_identifier;
    private readonly HashSet<string>? m_targets;
    private readonly LanguageRuleSet m_rules;
    private ShardPdfStage? m_pdf;

    #endregion

    #region Constructors

    /// <summary>Creates a processor for one pipeline; its PDFs (when the stage is on) run in the process-wide sandbox, their files in a fresh temp folder each.</summary>
    /// <param name="pipeline">The pipeline contract.</param>
    /// <param name="languageModel">The pinned language model; required when the language stage is on.</param>
    /// <exception cref="ArgumentException">The language stage is on and no model was given.</exception>
    public ShardProcessor(CorpusPipelineData pipeline, LanguageModel? languageModel)
        : this(pipeline, languageModel, null)
    {
    }

    /// <summary>Creates a processor for one pipeline with the PDF stage it uses.</summary>
    /// <param name="pipeline">The pipeline contract.</param>
    /// <param name="languageModel">The pinned language model; required when the language stage is on.</param>
    /// <param name="pdfStage">The PDF stage (on a node, with the task's temp scope for its files); null for one on the process-wide sandbox, made on the first PDF.</param>
    /// <exception cref="ArgumentException">The language stage is on and no model was given.</exception>
    public ShardProcessor(CorpusPipelineData pipeline, LanguageModel? languageModel, ShardPdfStage? pdfStage)
    {
        m_pipeline = pipeline;
        m_pdf = pdfStage;
        m_rules = languageModel?.Rules ?? LanguageRuleSet.Witlid1;

        if (pipeline.IdentifyLanguage)
        {
            if (languageModel == null)
                throw new ArgumentException("The pipeline identifies languages but no language model was given.", nameof(languageModel));

            m_identifier = new LanguageIdentifier(languageModel);
            m_targets = pipeline.TargetLanguages.Count > 0 ? new HashSet<string>(pipeline.TargetLanguages, StringComparer.Ordinal) : null;
        }
    }

    #endregion

    #region Functions

    /// <summary>Runs the shard through the pipeline.</summary>
    /// <param name="shard">The shard bytes (gzip WARC/WET), read once, forward only.</param>
    /// <param name="format">What the shard holds.</param>
    /// <param name="text">Receives the zstd-compressed JSON lines.</param>
    /// <param name="signatures">Receives the signatures blob (left empty when the pipeline does not sign).</param>
    /// <param name="cancellationToken">Stops between records.</param>
    /// <returns>The product's identity and counters.</returns>
    /// <exception cref="WarcFormatException">The bytes are not a WARC file.</exception>
    /// <exception cref="PdfSandboxException">The shard holds a PDF this node cannot run (no sandbox in the process, the wall-clock backstop): no document gets a verdict, another node makes the product.</exception>
    public ShardProduct Process(Stream shard, CorpusSourceFormat format, Stream text, Stream signatures, CancellationToken cancellationToken = default)
    {
        var stats = new ShardStatsBuilder();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var reader = new WarcReader(shard, ownsStream: false);

        if (m_pipeline.Sign)
            ShardSignatureWriter.WriteHeader(signatures);

        using (var compressor = CorpusCompressor.CompressTo(text))
        {
            var buffer = new MemoryStream();
            foreach (var record in reader.ReadRecords())
            {
                cancellationToken.ThrowIfCancellationRequested();
                stats.Record();

                foreach (var (url, document) in Extract(record, format, stats, cancellationToken))
                {
                    stats.Extracted();
                    Judge(url, document, stats, buffer, hash, compressor, signatures);
                }
            }
        }

        text.Flush();
        signatures.Flush();

        return new ShardProduct(Convert.ToHexStringLower(hash.GetHashAndReset()), stats, reader.SkippedOversized, reader.Truncated);
    }

    /// <summary>One extracted document through the language, line and quality stages; a survivor is written, hashed and signed.</summary>
    private void Judge(string url, string text, ShardStatsBuilder stats, MemoryStream buffer, IncrementalHash hash, Stream compressor, Stream signatures)
    {
        var language = "";
        if (m_identifier != null)
        {
            var guess = m_identifier.Identify(text);
            if (!guess.IsKnown)
            {
                if (guess.IsUnsupported)
                    stats.DropUnsupported(guess);
                else
                    stats.Drop(DROP_LANGUAGE_UNKNOWN);
                return;
            }

            if (m_targets != null && !m_targets.Contains(guess.Code))
            {
                stats.Drop(DROP_LANGUAGE_NOT_TARGET);
                return;
            }

            language = guess.Code;
        }

        var content = text;
        var rules = ShardQualityRules.Resolve(m_rules.For(language), m_pipeline.Quality);
        if (rules.FilterCurlyBracket && LineCleaner.HoldsCurlyBracket(content, rules))
        {
            stats.Drop(DROP_CURLY_BRACKET);
            return;
        }

        if (m_pipeline.CleanLines)
        {
            content = LineCleaner.Clean(content, rules, out _);
            if (content.Length == 0)
            {
                stats.Drop(DROP_NO_PROSE_LINES);
                return;
            }
        }

        if (m_pipeline.FilterQuality)
        {
            var verdict = QualityFilter.Judge(content, rules);
            if (!verdict.Passed)
            {
                stats.Drop(verdict.FailedRules[0]);
                return;
            }
        }

        WriteLine(buffer, url, language, content);
        var line = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        hash.AppendData(line);
        compressor.Write(line);

        if (m_pipeline.Sign)
            ShardSignatureWriter.Write(signatures, MinHashSigner.Sign(content));

        stats.Out(language);
    }

    /// <summary>The documents of one record: none, one (a WET conversion, an HTML page) or one per page range (a PDF).</summary>
    private IReadOnlyList<(string Url, string Text)> Extract(WarcRecord record, CorpusSourceFormat format, ShardStatsBuilder stats, CancellationToken cancellationToken)
    {
        var url = record.TargetUri ?? "";

        if (format == CorpusSourceFormat.Wet)
        {
            if (record.Type != RECORD_CONVERSION)
                return [];

            var wet = HtmlTextExtractor.CollapseLines(Encoding.UTF8.GetString(record.Payload.Span));
            if (wet.Length == 0)
                return Drop(stats, DROP_EMPTY_TEXT);

            return [(url, wet)];
        }

        if (record.Type != RECORD_RESPONSE)
            return [];

        var payload = HttpResponsePayload.Parse(record.Payload);
        if (payload == null)
            return Drop(stats, DROP_HTTP_UNPARSABLE);

        if (payload.StatusCode != 200)
            return Drop(stats, DROP_HTTP_STATUS);

        // With the stage off, a PDF is what it was before the stage existed: not HTML.
        if (m_pipeline.ExtractPdf && ShardPdfStage.IsCandidate(record, payload))
            return (m_pdf ??= new ShardPdfStage(null)).Extract(record, payload, stats, cancellationToken);

        if (!payload.IsHtml)
            return Drop(stats, DROP_NOT_HTML);

        if (!HtmlTextExtractor.TryExtract(payload.DecodeBody(), out var text, out _))
            return Drop(stats, DROP_HTML_REFUSED);

        if (text.Length == 0)
            return Drop(stats, DROP_EMPTY_TEXT);

        return [(url, text)];
    }

    private static IReadOnlyList<(string Url, string Text)> Drop(ShardStatsBuilder stats, string rule)
    {
        stats.Drop(rule);
        return [];
    }

    private static void WriteLine(MemoryStream buffer, string url, string language, string text)
    {
        buffer.SetLength(0);
        using (var writer = new Utf8JsonWriter(buffer, JSON_OPTIONS))
        {
            writer.WriteStartObject();
            writer.WriteString("url", url);
            writer.WriteString("lang", language);
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
    }

    #endregion
}
