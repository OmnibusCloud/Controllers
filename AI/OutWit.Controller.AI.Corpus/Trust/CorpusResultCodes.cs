using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// The fixed words a result's <see cref="CorpusResultData.Error"/> may hold. A node never sends
/// free text: no exception message, no path, no host name of its own network reaches a lab, and
/// the server refuses a result whose code is not one of these for its verdict. A code says what
/// happened in a word; the details stay in the node's own log.
/// </summary>
public static class CorpusResultCodes
{
    #region Constants

    /// <summary>The most characters a code has (the pattern of every code: lower-case ASCII letters, digits, underscores).</summary>
    public const int MAX_LENGTH = 32;

    /// <summary>A completed shard whose file ended inside a record: the records before it are the product.</summary>
    public const string NOTE_TRUNCATED = "truncated";

    /// <summary>OriginRefused: the URL is not one the task's allowlist permits (its host, its scheme, a user name in it).</summary>
    public const string ORIGIN_NOT_ALLOWED = "origin_not_allowed";

    /// <summary>OriginRefused: not a public origin, and the owner of the machine has not listed the host; or a public origin written another way than https on the default port.</summary>
    public const string ORIGIN_NOT_LISTED = "origin_not_listed";

    /// <summary>OriginRefused: the host resolves to no public address.</summary>
    public const string ORIGIN_ADDRESS = "origin_address";

    /// <summary>OriginRefused: a redirect leads somewhere that is not fetched, or there are too many.</summary>
    public const string ORIGIN_REDIRECT = "origin_redirect";

    /// <summary>FetchFailed: the origin answered with an HTTP status that is not a success; the three digits follow (<c>http_404</c>).</summary>
    public const string HTTP_PREFIX = "http_";

    /// <summary>How the HTTP codes are written in a list of codes: the three digits are the origin's status.</summary>
    public const string HTTP_PATTERN = "http_NNN";

    /// <summary>FetchFailed: the fetched bytes do not hash to the SHA-256 the manifest published.</summary>
    public const string HASH_MISMATCH = "hash_mismatch";

    /// <summary>FetchFailed: the body ended early and the origin gave no length to hold it to.</summary>
    public const string BODY_TRUNCATED = "body_truncated";

    /// <summary>Oversized: more bytes on the wire than the task's cap.</summary>
    public const string OVERSIZED = "oversized";

    /// <summary>Oversized: the shard unpacks to more than a shard may (a decompression bomb).</summary>
    public const string OVERSIZED_UNPACKED = "oversized_unpacked";

    /// <summary>Oversized: more bytes on the wire than the size the manifest declares for the shard (or the origin answered when the server asked) and its slack: the manifest promised less traffic than the shard takes.</summary>
    public const string OVERSIZED_DECLARED = "oversized_declared";

    /// <summary>Malformed: the bytes are not a WARC file where a record should start.</summary>
    public const string NOT_WARC = "not_warc";

    /// <summary>Malformed: the compressed stream is damaged.</summary>
    public const string BAD_COMPRESSION = "bad_compression";

    /// <summary>PipelineUnavailable: the pipeline identifies languages and names no model.</summary>
    public const string NO_LANGUAGE_MODEL = "no_language_model";

    /// <summary>PipelineUnavailable: the language model is not one this controller reads within its limits.</summary>
    public const string LANGUAGE_MODEL_REFUSED = "language_model_refused";

    /// <summary>Failed: the task carries no source or no pipeline, or parameters no pipeline may have.</summary>
    public const string TASK_INVALID = "task_invalid";

    /// <summary>NodeFault: a local file could not be created, written, read or removed.</summary>
    public const string FAULT_DISK = "disk";

    /// <summary>NodeFault: the disk has less room than the shard needs.</summary>
    public const string FAULT_DISK_SPACE = "disk_space";

    /// <summary>NodeFault: the origin did not answer this node (no connection, no name, a stalled or broken body) after every retry; the node cannot tell its own link from the origin.</summary>
    public const string FAULT_NETWORK = "network";

    /// <summary>NodeFault: the language model did not arrive on the node, or arrived with other bytes than its pin.</summary>
    public const string FAULT_LANGUAGE_MODEL = "language_model";

    /// <summary>NodeFault: the PDF sandbox of this node failed to run a document (not the document's doing).</summary>
    public const string FAULT_PDF_SANDBOX = "pdf_sandbox";

    /// <summary>NodeFault: a PDF ran into the wall-clock backstop with fuel left: the machine, not the document, stopped it.</summary>
    public const string FAULT_PDF_BACKSTOP = "pdf_backstop";

    /// <summary>NodeFault: a product could not be uploaded.</summary>
    public const string FAULT_UPLOAD = "upload";

    /// <summary>NodeFault: the node ran out of memory.</summary>
    public const string FAULT_MEMORY = "memory";

    /// <summary>NodeFault: anything else that went wrong inside the node.</summary>
    public const string FAULT_INTERNAL = "internal";

    /// <summary>NodeFault: the task package did not arrive on the node, or arrived as other bytes than its reference names.</summary>
    public const string FAULT_TASKS = "tasks";

    /// <summary>Every code a <see cref="CorpusShardVerdict.NodeFault"/> may carry.</summary>
    public static readonly IReadOnlyList<string> FAULTS =
    [
        FAULT_DISK, FAULT_DISK_SPACE, FAULT_NETWORK, FAULT_LANGUAGE_MODEL, FAULT_PDF_SANDBOX,
        FAULT_PDF_BACKSTOP, FAULT_UPLOAD, FAULT_MEMORY, FAULT_INTERNAL, FAULT_TASKS
    ];

    private static readonly IReadOnlySet<string> FAULT_SET = new HashSet<string>(FAULTS, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<CorpusShardVerdict, IReadOnlySet<string>> BY_VERDICT = new Dictionary<CorpusShardVerdict, IReadOnlySet<string>>
    {
        [CorpusShardVerdict.Completed] = Set("", NOTE_TRUNCATED),
        [CorpusShardVerdict.OriginRefused] = Set(ORIGIN_NOT_ALLOWED, ORIGIN_NOT_LISTED, ORIGIN_ADDRESS, ORIGIN_REDIRECT),
        [CorpusShardVerdict.FetchFailed] = Set(HASH_MISMATCH, BODY_TRUNCATED),
        [CorpusShardVerdict.Oversized] = Set(OVERSIZED, OVERSIZED_UNPACKED, OVERSIZED_DECLARED),
        [CorpusShardVerdict.Malformed] = Set(NOT_WARC, BAD_COMPRESSION),
        [CorpusShardVerdict.PipelineUnavailable] = Set(NO_LANGUAGE_MODEL, LANGUAGE_MODEL_REFUSED),
        [CorpusShardVerdict.Failed] = Set(TASK_INVALID),
        [CorpusShardVerdict.NodeFault] = FAULT_SET
    };

    #endregion

    #region Functions

    /// <summary>The code of an HTTP status the origin answered with.</summary>
    /// <param name="status">The status, 100 to 599.</param>
    /// <returns>For example <c>http_503</c>.</returns>
    public static string Http(int status)
    {
        return HTTP_PREFIX + Math.Clamp(status, 100, 599).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The codes a result of a verdict may carry beside the empty one, in ordinal order; <see cref="HTTP_PATTERN"/> stands for the origin's HTTP statuses.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The codes; empty for a verdict that carries none.</returns>
    public static IReadOnlyList<string> CodesOf(CorpusShardVerdict verdict)
    {
        if (!BY_VERDICT.TryGetValue(verdict, out var codes))
            return [];

        var list = codes.Where(code => code.Length > 0).ToList();
        if (verdict == CorpusShardVerdict.FetchFailed)
            list.Add(HTTP_PATTERN);

        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>Whether a code is one a result of the given verdict may carry.</summary>
    /// <param name="verdict">The result's verdict.</param>
    /// <param name="code">The result's error; may be null (the wire carries nulls).</param>
    /// <returns>True for a code of the fixed vocabulary.</returns>
    public static bool IsKnown(CorpusShardVerdict verdict, string? code)
    {
        if (code == null || !BY_VERDICT.TryGetValue(verdict, out var codes))
            return false;

        return codes.Contains(code) || (verdict == CorpusShardVerdict.FetchFailed && IsHttp(code));
    }

    /// <summary>Whether a code is a node fault's.</summary>
    /// <param name="code">The code; may be null.</param>
    /// <returns>True for one of <see cref="FAULTS"/>.</returns>
    public static bool IsFault(string? code)
    {
        return code != null && FAULT_SET.Contains(code);
    }

    private static bool IsHttp(string code)
    {
        return code.Length == HTTP_PREFIX.Length + 3
               && code.StartsWith(HTTP_PREFIX, StringComparison.Ordinal)
               && code[HTTP_PREFIX.Length] is >= '1' and <= '5'
               && char.IsAsciiDigit(code[HTTP_PREFIX.Length + 1])
               && char.IsAsciiDigit(code[HTTP_PREFIX.Length + 2]);
    }

    private static IReadOnlySet<string> Set(params string[] codes)
    {
        return new HashSet<string>(codes, StringComparer.Ordinal);
    }

    #endregion
}
