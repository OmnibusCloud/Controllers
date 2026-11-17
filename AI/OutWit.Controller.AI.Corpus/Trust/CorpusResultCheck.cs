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
/// honest one can be, no count below zero, and sums that agree with its totals; the writers can
/// then read it without a guard. Whether the result is the truth about its shard is another
/// question, which only a second machine answers.
/// </summary>
public static class CorpusResultCheck
{
    #region Constants

    /// <summary>The most shards a node says it processes at once.</summary>
    public const int MAX_DEGREE = 4096;

    /// <summary>The most characters of a controller version or a runtime target.</summary>
    public const int MAX_LABEL_CHARS = 32;

    /// <summary>The most characters of a source URL.</summary>
    public const int MAX_URL_CHARS = 4096;

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
            || result.Degree is < 0 or > MAX_DEGREE
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

        if (!IsCounts(stats.Drops, DROP_RULES.Count, DROP_RULES.Contains, out var drops)
            || !IsCounts(stats.Languages, LanguageModelLimits.MAX_PROFILES, name => name.Length == 0 || LanguageModelLimits.IsCode(name), out var languages)
            || !IsCounts(stats.UnsupportedLanguages, 2 * LanguageModelLimits.MAX_PROFILES + 1, IsUnsupportedKey, out var unsupported))
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

        return stats.Pdf == null || IsPdf(stats.Pdf);
    }

    private static bool IsPdf(CorpusPdfStatsData pdf)
    {
        return pdf.In > 0 && pdf.Truncated >= 0 && pdf.Truncated <= pdf.In
               && pdf.Extracted >= 0 && pdf.Extracted <= pdf.In
               && pdf.Pages >= 0 && pdf.Documents >= 0 && pdf.PagesCut >= 0 && pdf.RunningLines >= 0
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
}
