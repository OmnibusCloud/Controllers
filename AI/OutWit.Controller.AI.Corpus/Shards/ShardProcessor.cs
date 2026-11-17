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
/// zstd-compressed, with a MinHash signature per document in a side blob (with the pipeline's
/// MinHash parameters; none when the pipeline does not sign), the blob hashed as it is written. A
/// pure function of the shard bytes and the pipeline: two nodes produce the same hashes or one of
/// them is wrong; the output mode changes neither (statistics and hashes only keeps the products on
/// the node, it makes them all the same).
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

    /// <summary>A record larger than a record of its format may be (<see cref="ShardLimits.MaxRecordBytes"/>): skipped, never held.</summary>
    public const string DROP_RECORD_OVERSIZED = "record_oversized";

    /// <summary>A page whose charset has a name the platform knows and does not decode (UTF-7): one page without text, not a shard without a product.</summary>
    public const string DROP_UNSUPPORTED_CHARSET = "unsupported_charset";

    /// <summary>A page the HTML parser itself found past its budget: its tree reached the nesting limit, or it made more elements than a page may have.</summary>
    public const string DROP_HTML_TOO_COMPLEX = "html_too_complex";
    /// <summary>
    /// A page its decoder or the HTML parser failed on in a way of their own (an error of logic
    /// over the page's bytes, not a limit): the page drops, as a PDF the extractor fails on does
    /// (<c>pdf_error</c>), and the shard is made.
    /// </summary>
    public const string DROP_HTML_ERROR = "html_error";

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
    private readonly MinHashParameters m_minHash;
    private readonly bool m_sign;
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
    /// <exception cref="ArgumentException">The language stage is on and no model was given, or the MinHash parameters cannot be used.</exception>
    public ShardProcessor(CorpusPipelineData pipeline, LanguageModel? languageModel, ShardPdfStage? pdfStage)
    {
        m_pipeline = pipeline;
        m_pdf = pdfStage;
        m_rules = languageModel?.Rules ?? LanguageRuleSet.Witlid1;
        m_sign = Signs(pipeline);
        m_minHash = MinHashParameters.Resolve(pipeline.MinHash);

        if (m_sign && MinHashParameters.Problem(pipeline.MinHash) is { } problem)
            throw new ArgumentException($"The pipeline's MinHash parameters cannot be used: {problem}", nameof(pipeline));

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

    /// <summary>Whether a pipeline makes signatures (kept or not: under statistics and hashes only they are made and hashed for integrity, and stay on the node).</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>True when the processor writes a signatures blob.</returns>
    public static bool Signs(CorpusPipelineData pipeline)
    {
        return pipeline.Sign;
    }

    /// <summary>Whether a run keeps the signatures on the server: it signs and keeps its products.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>True when the node uploads the signatures blob.</returns>
    public static bool KeepsSignatures(CorpusPipelineData pipeline)
    {
        return pipeline.Sign && pipeline.Output == CorpusOutputMode.Full;
    }

    /// <summary>Runs the shard through the pipeline.</summary>
    /// <param name="shard">The shard bytes (gzip WARC/WET), read once, forward only.</param>
    /// <param name="format">What the shard holds.</param>
    /// <param name="text">Receives the zstd-compressed JSON lines.</param>
    /// <param name="signatures">Receives the signatures blob (left empty when the pipeline makes none: see <see cref="Signs"/>).</param>
    /// <param name="cancellationToken">Stops between records.</param>
    /// <returns>The product's identity and counters.</returns>
    /// <exception cref="WarcFormatException">The bytes are not a WARC file.</exception>
    /// <exception cref="PdfSandboxException">The shard holds a PDF this node cannot run (no sandbox in the process, the wall-clock backstop): no document gets a verdict, another node makes the product.</exception>
    public ShardProduct Process(Stream shard, CorpusSourceFormat format, Stream text, Stream signatures, CancellationToken cancellationToken = default)
    {
        var stats = new ShardStatsBuilder();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // The reader holds one record at a time, of a size its format allows, stops at the most
        // the file may unpack to, and looks at the token inside a record as between records.
        using var reader = new WarcReader(shard, ShardLimits.MaxRecordBytes(format), ownsStream: false, ShardLimits.MaxUnpackedBytes(shard.Length), cancellationToken);
        using var signed = new ShardHashingStream(signatures);

        if (m_sign)
            ShardSignatureWriter.WriteHeader(signed, m_minHash);

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
                    Judge(url, document, stats, buffer, hash, compressor, signed);
                }
            }

            // A record too large to hold was read past, not into memory: it is a record of the
            // shard and a drop of its own, so that nothing a shard held goes uncounted.
            for (var skipped = 0; skipped < reader.SkippedOversized; skipped++)
            {
                stats.Record();
                stats.Drop(DROP_RECORD_OVERSIZED);
            }
        }

        text.Flush();
        signatures.Flush();

        return new ShardProduct(Convert.ToHexStringLower(hash.GetHashAndReset()), stats, reader.SkippedOversized, reader.Truncated)
        {
            SignaturesSha256 = m_sign ? signed.Sha256() : ""
        };
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
            // The first failed rule is the drop's name: nothing is measured past it, and a
            // document of millions of words fails its length rule before it is split into them.
            var verdict = QualityFilter.Judge(content, rules, firstFailureOnly: true);
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

        if (m_sign)
            ShardSignatureWriter.Write(signatures, MinHashSigner.Sign(content, m_minHash.Hashes, m_minHash.ShingleWords));

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

        HttpResponsePayload? payload;
        try
        {
            payload = HttpResponsePayload.Parse(record.Payload);
        }
        catch (Exception exception) when (IsOfThePage(exception))
        {
            payload = null;
        }

        if (payload == null)
            return Drop(stats, DROP_HTTP_UNPARSABLE);

        if (payload.StatusCode != 200)
            return Drop(stats, DROP_HTTP_STATUS);

        // With the stage off, a PDF is what it was before the stage existed: not HTML.
        if (m_pipeline.ExtractPdf && ShardPdfStage.IsCandidate(record, payload))
            return (m_pdf ??= new ShardPdfStage(null)).Extract(record, payload, stats, cancellationToken);

        if (!payload.IsHtml)
            return Drop(stats, DROP_NOT_HTML);

        HtmlReading read;
        string text;
        try
        {
            if (!payload.TryDecodeBody(out var html))
                return Drop(stats, DROP_UNSUPPORTED_CHARSET);

            (read, text) = HtmlReader != null ? HtmlReader(html) : ReadPage(html);
        }
        catch (Exception exception) when (IsOfThePage(exception))
        {
            // One page must never be the end of its shard: whoever controls a crawled page
            // would remove a gigabyte of others with it.
            return Drop(stats, DROP_HTML_ERROR);
        }

        if (read != HtmlReading.Text)
            return Drop(stats, read == HtmlReading.Refused ? DROP_HTML_REFUSED : DROP_HTML_TOO_COMPLEX);

        if (text.Length == 0)
            return Drop(stats, DROP_EMPTY_TEXT);

        return [(url, text)];
    }

    private static (HtmlReading Reading, string Text) ReadPage(string html)
    {
        var reading = HtmlTextExtractor.Read(html, out var text);
        return (reading, text);
    }

    /// <summary>
    /// Whether an exception out of the decoding or the parsing of one page is the page's: an
    /// error of logic over its bytes, which the same bytes cause on every node under the same
    /// controller version, so it is a statement about the input and the page drops. Everything
    /// else is the node's and goes up, to come back as a node fault: memory, cancellation, a
    /// library that is missing, mismatched or will not load, anything of the file system. A node
    /// whose parser does not load must not report a shard of dropped pages.
    /// </summary>
    private static bool IsOfThePage(Exception exception)
    {
        return exception is (InvalidOperationException and not ObjectDisposedException)
            or ArgumentException
            or IndexOutOfRangeException
            or NullReferenceException
            or FormatException
            or KeyNotFoundException
            or InvalidCastException
            or ArithmeticException
            or (NotSupportedException and not PlatformNotSupportedException);
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

    #region Properties

    /// <summary>Reads a page in the HTML parser's place (tests of a parser that fails); null for the parser.</summary>
    internal Func<string, (HtmlReading Reading, string Text)>? HtmlReader { get; init; }

    #endregion
}
