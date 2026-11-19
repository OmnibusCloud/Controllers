using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// The shape an honest node gives a result, checked once, on arrival, before anything of it is
/// used: a node chooses every byte of what it returns, the wire carries nulls where the types say
/// there are none, and one result shaped otherwise must cost that result, never the run. A result
/// that passes has no null, no name outside the controller's vocabulary, no list longer than an
/// honest one can be, no count below zero, sums that agree with its totals, and no count above
/// what the bytes it says it fetched can hold (<see cref="ShardLimits.MaxRecords"/> records, a
/// document for each record that is no PDF, and for a PDF no more than its text can be cut
/// into); the writers can then read it without a guard. Whether the result is the truth about its
/// shard is another question, which only a second machine answers: within these bounds a node
/// still says what it likes of a shard nobody else ran.
/// </summary>
public static class CorpusResultCheck
{
    #region Constants

    /// <summary>The most characters of a controller version or a runtime target.</summary>
    public const int MAX_LABEL_CHARS = 32;

    /// <summary>The most characters of a source URL.</summary>
    public const int MAX_URL_CHARS = 4096;

    /// <summary>The most languages a result's statistics may name: every profile a language pack may carry.</summary>
    public const int MAX_LANGUAGES = LanguageModelLimits.MAX_PROFILES;

    /// <summary>The most keys the breakdown of the unsupported documents may have: a language's code, the same code past its limit, and the one for no profile at all.</summary>
    public const int MAX_UNSUPPORTED = 2 * LanguageModelLimits.MAX_PROFILES + 1;

    /// <summary>
    /// The most pages the text of one PDF can be: the stage takes at most
    /// <see cref="Pdf.PdfStageLimits.MAX_TEXT_BYTES"/> bytes of text from a PDF, and a page more
    /// is a page break more.
    /// </summary>
    public const long MAX_PDF_PAGES = (long)Pdf.PdfStageLimits.MAX_TEXT_BYTES + 1;

    private const string VERSION_MARKS = ".-+";
    private const string TARGET_MARKS = "-_";

    private static readonly IReadOnlySet<string> DROP_RULES = new HashSet<string>(ShardDropRules.ALL.Select(pair => pair.Rule), StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Checks a result's shape.</summary>
    /// <param name="result">What a node returned; may be null.</param>
    /// <returns>True when the result is shaped as an honest node shapes one.</returns>
    public static bool IsWellFormed(CorpusResultData? result)
    {
        if (result == null)
            return false;

        if (!CorpusTickets.IsWellFormed(result.Ticket)
            || !Enum.IsDefined(result.Verdict)
            || !Enum.IsDefined(result.Output)
            || result.TaskIndex < 0
            || result.Degree != 0
            || result.NodeId != Guid.Empty
            || result.NodeName is not { Length: 0 }
            || result.SourceUrl == null || result.SourceUrl.Length > MAX_URL_CHARS
            || !CorpusText.IsName(result.ControllerVersion, MAX_LABEL_CHARS, VERSION_MARKS)
            || !CorpusText.IsName(result.RuntimeTarget, MAX_LABEL_CHARS, TARGET_MARKS, allowEmpty: true)
            || !CorpusText.IsSha256OrEmpty(result.ContentSha256)
            || !CorpusText.IsSha256OrEmpty(result.SignaturesSha256)
            || !CorpusResultCodes.IsKnown(result.Verdict, result.Error))
            return false;

        return result.Verdict == CorpusShardVerdict.Completed ? IsCompleted(result) : IsUnanswered(result);
    }

    /// <summary>A result that did not complete carries no product: no blob, no hash, no statistics.</summary>
    private static bool IsUnanswered(CorpusResultData result)
    {
        return result.TextBlobId == Guid.Empty
               && result.SignaturesBlobId == Guid.Empty
               && result.ContentSha256.Length == 0
               && result.SignaturesSha256.Length == 0
               && result.MinHash == null
               && result.Stats == null;
    }

    private static bool IsCompleted(CorpusResultData result)
    {
        if (result.ContentSha256.Length == 0 || result.Stats == null)
            return false;

        if (result.MinHash == null)
        {
            if (result.SignaturesSha256.Length != 0 || result.SignaturesBlobId != Guid.Empty)
                return false;
        }
        else if (result.SignaturesSha256.Length == 0 || MinHashParameters.Problem(result.MinHash) != null || result.MinHash.Hashes <= 0 || result.MinHash.ShingleWords <= 0 || result.MinHash.Bands <= 0)
        {
            // What a node echoes is resolved: no zero stands for a default.
            return false;
        }

        return IsStats(result.Stats);
    }

    private static bool IsStats(CorpusShardStatsData stats)
    {
        if (stats.RecordsIn < 0 || stats.DocumentsExtracted < 0 || stats.DocumentsOut < 0
            || stats.BytesFetched < 0 || stats.BytesFetched > CorpusTaskPlanner.MAX_SHARD_BYTES
            || stats.BytesProduced < 0 || stats.FetchMs < 0 || stats.ProcessMs < 0 || stats.WaitMs < 0)
            return false;

        // A record is a number of bytes of the unpacked file at the least: no more records than
        // the bytes the result says it fetched may unpack to.
        if (stats.RecordsIn > ShardLimits.MaxRecords(stats.BytesFetched))
            return false;

        if (!IsCounts(stats.Drops, MaxDrops, DROP_RULES.Contains, out var drops)
            || !IsCounts(stats.Languages, MAX_LANGUAGES, name => name.Length == 0 || LanguageModelLimits.IsCode(name), out var languages)
            || !IsCounts(stats.UnsupportedLanguages, MAX_UNSUPPORTED, IsUnsupportedKey, out var unsupported))
            return false;

        // The funnel: the documents extracted less the language, line and quality drops are the
        // documents out; the extraction drops count records beside it.
        long inside = 0;
        long unsupportedDrops = 0;
        foreach (var drop in stats.Drops)
        {
            if (ShardDropRules.StageOf(drop.Name) != ShardDropRules.STAGE_EXTRACT)
                inside += drop.Count;
            if (drop.Name == ShardProcessor.DROP_LANGUAGE_UNSUPPORTED)
                unsupportedDrops = drop.Count;
        }

        if (stats.DocumentsExtracted - inside != stats.DocumentsOut || languages != stats.DocumentsOut || unsupported != unsupportedDrops)
            return false;

        // Every extraction drop is a record or a PDF that gave nothing: no more of them than records.
        if (drops - inside > (long)stats.RecordsIn + (stats.Pdf?.In ?? 0))
            return false;

        if (stats.Pdf != null && !IsPdf(stats.Pdf, stats.RecordsIn))
            return false;

        // A record gives one document at the most, unless it is a PDF: a PDF gives its page
        // ranges, and those are counted with the PDFs.
        long pdfs = stats.Pdf?.In ?? 0;
        long pdfDocuments = stats.Pdf?.Documents ?? 0;

        return stats.DocumentsExtracted >= pdfDocuments && stats.DocumentsExtracted - pdfDocuments <= stats.RecordsIn - pdfs;
    }

    /// <summary>
    /// The PDF stage's counts as the stage makes them: a PDF is a record of the shard; one that
    /// gave text has a page at the least and a document at the least, no more documents than
    /// pages, no more pages than its text can be, no more pages cut than documents, and no more
    /// lines removed than its text has characters.
    /// </summary>
    private static bool IsPdf(CorpusPdfStatsData pdf, int records)
    {
        return pdf.In > 0 && pdf.In <= records
               && pdf.Truncated >= 0 && pdf.Truncated <= pdf.In
               && pdf.Extracted >= 0 && pdf.Extracted <= pdf.In
               && pdf.Pages >= pdf.Extracted && pdf.Pages <= pdf.Extracted * MAX_PDF_PAGES
               && pdf.Documents >= pdf.Extracted && pdf.Documents <= pdf.Pages
               && pdf.PagesCut >= 0 && pdf.PagesCut <= pdf.Documents
               && pdf.RunningLines >= 0 && pdf.RunningLines <= (long)pdf.Extracted * Pdf.PdfStageLimits.MAX_TEXT_BYTES
               // A shard's PDFs stop being run once their fuel together is spent: no honest sum passes it by a whole budget.
               && pdf.Fuel >= 0 && pdf.Fuel <= Pdf.PdfStageLimits.SHARD_FUEL + Pdf.PdfStageLimits.FUEL;
    }

    /// <summary>A list of named counts as the node's statistics builder writes one: names known, in ordinal order (so none twice), counts above zero.</summary>
    private static bool IsCounts(List<CorpusCountData>? counts, int maxItems, Func<string, bool> isName, out long sum)
    {
        sum = 0;
        if (counts == null || counts.Count > maxItems)
            return false;

        string? previous = null;
        foreach (var count in counts)
        {
            if (count?.Name == null || count.Count <= 0 || !isName(count.Name))
                return false;

            if (previous != null && string.CompareOrdinal(previous, count.Name) >= 0)
                return false;

            previous = count.Name;
            sum += count.Count;
        }

        return true;
    }

    private static bool IsUnsupportedKey(string name)
    {
        if (name == ShardStatsBuilder.UNSUPPORTED_NO_PROFILE)
            return true;

        return LanguageModelLimits.IsCode(name.StartsWith(ShardStatsBuilder.UNSUPPORTED_PAST_LIMIT, StringComparison.Ordinal)
            ? name[ShardStatsBuilder.UNSUPPORTED_PAST_LIMIT.Length..]
            : name);
    }

    #endregion

    #region Properties

    /// <summary>The most drop rules a result's statistics may name: every rule the stages have.</summary>
    public static int MaxDrops => DROP_RULES.Count;

    #endregion
}
