using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// Writes the integrity document of a run from the server's ledger: what the run's results rest
/// on, and everything that did not go the plain way.
/// <list type="bullet">
/// <item><b>pairs</b>: every shard that came back twice in the first round, its two results
/// compared on everything an honest node cannot vary — the verdict, the content hash of the
/// product, the hash of its signatures, the document count, the drop and language histograms, the
/// PDF stage's counts. Signatures that differ while the text agrees are a mismatch of their own
/// kind (<c>signatures</c>, counted apart). A pair with a side that is no result (a node fault, a
/// dropped value) is counted, not compared (<c>incomplete</c>). The platform keeps the two on
/// different machines, and the document does not say which.</item>
/// <item><b>disputes</b>: every shard whose copies disagreed, what three machines returned for it
/// and how it ended (<c>outcome</c>): <c>resolved</c> (the most of them, two at least and without
/// a tie, returned the same), <c>no_majority</c>, <c>too_few_copies</c> or
/// <c>surplus_copies</c>.</item>
/// <item><b>no_result</b>: every shard no machine returned an acceptable result for, with the
/// fixed reason and the fault codes the nodes gave.</item>
/// <item><b>rejected</b>: the values dropped, by reason: no ticket of this job and round
/// (<c>unknown_ticket</c>), no result or a shape no honest node gives (<c>unreadable</c>), another
/// controller version (<c>version</c>), not what its task asked for (<c>content</c>), more values
/// under one ticket than a round takes (<c>surplus</c>). Every reason is written, 0 included.</item>
/// </list>
/// A shard in none of these lists was run once, and its result is one machine's word
/// (<c>single_execution</c> says how many). Schema <c>corpus-integrity/5</c>; <c>/4</c> let a
/// result opt out of the comparison by the version it claimed and chose the execution that stood
/// for a shard by its own content hash. <c>version_skew</c> is kept for readers of <c>/4</c> and
/// is always 0: a result of another version is dropped (<c>rejected</c>, reason <c>version</c>).
/// </summary>
public static class CorpusIntegrityWriter
{
    #region Constants

    /// <summary>The document schema name.</summary>
    public const string SCHEMA = "corpus-integrity/5";

    /// <summary>A compared pair's signatures: both sides made the same.</summary>
    public const string SIGNATURES_MATCH = "match";

    /// <summary>A compared pair's signatures: the sides made different ones (a mismatch, <c>signatures</c> in its differences).</summary>
    public const string SIGNATURES_DIFFER = "differ";

    /// <summary>A compared pair's signatures: neither side made any (the pipeline does not sign); nothing to compare.</summary>
    public const string SIGNATURES_NONE = "none";

    private const string KIND_EXECUTION = "execution";
    private const string KIND_FAULT = "fault";
    private const string KIND_REJECTED = "rejected";

    #endregion

    #region Functions

    /// <summary>Serializes the ledger's account as UTF-8 JSON.</summary>
    /// <param name="ledger">The server's account of the run.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(CorpusLedger ledger)
    {
        var pairs = ledger.Shards.Where(shard => shard.First.Count >= 2).Select(Pair).ToList();
        var disputes = ledger.Shards.Where(shard => shard.NeedsDispute).ToList();
        var unanswered = ledger.Shards.Where(shard => shard.Outcome == CorpusShardOutcome.NoResult).ToList();

        var compared = pairs.Count(pair => pair.Compared);
        var mismatches = pairs.Count(pair => pair.Compared && !pair.Match);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ledger.ControllerVersion);

            writer.WriteStartObject("summary");
            writer.WriteNumber("shards", ledger.Shards.Count);
            writer.WriteNumber("single_execution", ledger.SingleExecutions);
            writer.WriteNumber("sampled", pairs.Count);
            writer.WriteNumber("compared", compared);
            writer.WriteNumber("version_skew", 0);
            writer.WriteNumber("incomplete", pairs.Count(pair => !pair.Compared));
            writer.WriteNumber("mismatches", mismatches);
            writer.WriteNumber("mismatch_rate", compared == 0 ? 0.0 : (double)mismatches / compared);
            writer.WriteNumber("signatures_compared", pairs.Count(pair => pair.Compared && pair.Signatures != SIGNATURES_NONE));
            writer.WriteNumber("signature_mismatches", pairs.Count(pair => pair.Compared && pair.Signatures == SIGNATURES_DIFFER));
            writer.WriteNumber("rejected_results", ledger.RejectedResults);
            writer.WriteNumber("node_faults", ledger.NodeFaults);
            writer.WriteNumber("repairs", ledger.Repairs);
            writer.WriteNumber("repaired", ledger.Repaired);
            writer.WriteNumber("no_result", unanswered.Count);
            writer.WriteNumber("disputes", ledger.Disputes);
            writer.WriteNumber("disputes_resolved", ledger.DisputesResolved);
            writer.WriteNumber("disputed", ledger.Disputes - ledger.DisputesResolved);
            writer.WriteEndObject();

            writer.WriteStartObject("rejected");
            foreach (var reason in CorpusLedger.REJECTION_REASONS)
                writer.WriteNumber(reason, ledger.Rejected.GetValueOrDefault(reason));
            writer.WriteEndObject();

            writer.WriteStartArray("pairs");
            foreach (var pair in pairs)
                WritePair(writer, pair);
            writer.WriteEndArray();

            writer.WriteStartArray("disputes");
            foreach (var shard in disputes)
                WriteDispute(writer, shard);
            writer.WriteEndArray();

            writer.WriteStartArray("no_result");
            foreach (var shard in unanswered)
                WriteNoResult(writer, shard);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The fields two results of one shard disagree on; empty when they agree.</summary>
    /// <param name="primary">A checked result.</param>
    /// <param name="replica">Another one of the same shard.</param>
    /// <returns>Field names: verdict, content, signatures, documents, drops, languages, pdf.</returns>
    public static List<string> Differences(CorpusResultData primary, CorpusResultData replica)
    {
        return CorpusLedger.Differences(primary, replica);
    }

    private static PairRecord Pair(CorpusLedgerShard shard)
    {
        var primary = shard.First[0];
        var replica = shard.First[1];
        var compared = primary.Kind == CorpusLedgerEntryKind.Execution && replica.Kind == CorpusLedgerEntryKind.Execution;
        var differing = compared ? CorpusLedger.Differences(primary.Result!, replica.Result!) : [];

        var signatures = !compared ? SIGNATURES_NONE
            : primary.Result!.SignaturesSha256.Length == 0 && replica.Result!.SignaturesSha256.Length == 0 ? SIGNATURES_NONE
            : CorpusLedger.SignaturesDiffer(differing) ? SIGNATURES_DIFFER
            : SIGNATURES_MATCH;

        return new PairRecord(shard, primary, replica, compared, differing.Count == 0, differing, signatures);
    }

    private static void WritePair(Utf8JsonWriter writer, PairRecord pair)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", pair.Shard.Index);
        WriteSide(writer, "primary", pair.Primary);
        WriteSide(writer, "replica", pair.Replica);
        if (pair.Shard.First.Count > 2)
            writer.WriteNumber("results", pair.Shard.First.Count);
        writer.WriteBoolean("compared", pair.Compared);
        if (!pair.Compared)
            writer.WriteBoolean("incomplete", true);
        else
        {
            writer.WriteBoolean("match", pair.Match);
            writer.WriteString("signatures", pair.Signatures);
            if (!pair.Match)
            {
                writer.WriteStartArray("differs");
                foreach (var field in pair.Differing)
                    writer.WriteStringValue(field);
                writer.WriteEndArray();
            }
        }

        if (pair.Shard.NeedsDispute)
            writer.WriteBoolean("dispute", true);

        writer.WriteEndObject();
    }

    private static void WriteDispute(Utf8JsonWriter writer, CorpusLedgerShard shard)
    {
        var resolved = shard.Outcome == CorpusShardOutcome.Accepted;

        writer.WriteStartObject();
        writer.WriteNumber("index", shard.Index);
        writer.WriteNumber("first_copies", shard.First.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
        writer.WriteNumber("copies", shard.Dispute.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
        writer.WriteNumber("agreeing", shard.DisputeAgreeing);
        writer.WriteString("outcome", shard.DisputeOutcome);
        writer.WriteStartArray("results");
        foreach (var entry in shard.Dispute)
            WriteSide(writer, null, entry);
        writer.WriteEndArray();
        if (resolved)
            WriteSide(writer, "accepted", new CorpusLedgerEntry(CorpusLedgerEntryKind.Execution, shard.Accepted, ""));
        writer.WriteEndObject();
    }

    private static void WriteNoResult(Utf8JsonWriter writer, CorpusLedgerShard shard)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", shard.Index);
        writer.WriteString("reason", shard.Reason);
        writer.WriteStartArray("faults");
        foreach (var fault in shard.Faults)
            writer.WriteStringValue(fault);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSide(Utf8JsonWriter writer, string? name, CorpusLedgerEntry entry)
    {
        if (name == null)
            writer.WriteStartObject();
        else
            writer.WriteStartObject(name);

        var result = entry.Kind == CorpusLedgerEntryKind.Execution ? entry.Result : null;
        writer.WriteString("kind", entry.Kind switch
        {
            CorpusLedgerEntryKind.Execution => KIND_EXECUTION,
            CorpusLedgerEntryKind.Fault => KIND_FAULT,
            _ => KIND_REJECTED
        });
        if (entry.Kind != CorpusLedgerEntryKind.Execution)
            writer.WriteString("code", entry.Code);
        writer.WriteString("controller", result?.ControllerVersion ?? "");
        writer.WriteString("verdict", result?.Verdict.ToString() ?? "");
        writer.WriteString("content_sha256", result?.ContentSha256 ?? "");
        writer.WriteString("signatures_sha256", result?.SignaturesSha256 ?? "");
        writer.WriteNumber("documents_out", result?.Stats?.DocumentsOut ?? 0);
        writer.WriteEndObject();
    }

    #endregion

    #region Types

    /// <summary>The first two results of a shard that came back more than once: compared only when both are results with a verdict.</summary>
    private sealed record PairRecord(CorpusLedgerShard Shard, CorpusLedgerEntry Primary, CorpusLedgerEntry Replica, bool Compared, bool Match, List<string> Differing, string Signatures);

    #endregion
}
