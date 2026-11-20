using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// Writes the integrity document of a run from the server's ledger: what the run's results rest
/// on, and everything that did not go the plain way.
/// <list type="bullet">
/// <item><b>sampling</b> and <b>rounds</b>: the share of the packages the run asked to run on two
/// machines and what came back of it, in a word and in a sentence, and for each round the
/// packages that went out and the answers that came back (<see cref="CorpusFanOutWriter"/>). A
/// run whose checking did not happen says so in words.</item>
/// <item><b>pairs</b>: every shard that came back twice in the first round or in the repair
/// round (<c>round</c> says which), its two results
/// compared on everything an honest node cannot vary — the verdict, the content hash of the
/// product, the hash of its signatures, the document count, the drop and language histograms, the
/// PDF stage's counts. Signatures that differ while the text agrees are a mismatch of their own
/// kind (<c>signatures</c>, counted apart). A pair with a side that is no result (a node fault, a
/// dropped value) is counted, not compared (<c>incomplete</c>). The platform keeps the two on
/// different machines, and the document does not say which. The summary counts the first
/// round's pairs (<c>sampled</c>, <c>compared</c>, <c>mismatches</c> and the rate they give:
/// those are the run's sample) apart from the repair round's (<c>repair_pairs</c>,
/// <c>repair_compared</c>, <c>repair_mismatches</c>: shards that went out again, on two machines
/// when the run asked for sampling).</item>
/// <item><b>disputes</b>: every shard whose copies disagreed, what three machines returned for it
/// and how it ended (<c>outcome</c>): <c>resolved</c> (the most of them, two at least and without
/// a tie, returned the same), <c>no_majority</c>, <c>too_few_copies</c> or
/// <c>surplus_copies</c>. A dispute that came back with too few copies, or with a lead that may be
/// one machine's second answers, went out once more on three machines: its record then holds
/// that round as well (<c>again</c>), and <c>outcome</c> is how the second round ended.</item>
/// <item><b>no_result</b>: every shard no machine returned an acceptable result for, with the
/// fixed reason and the fault codes the nodes gave.</item>
/// <item><b>rejected</b>: the values dropped, by reason: no ticket of this job and round
/// (<c>unknown_ticket</c>), no result or a shape no honest node gives (<c>unreadable</c>), another
/// controller version (<c>version</c>), not what its task asked for (<c>content</c>), more values
/// under one ticket than a round takes (<c>surplus</c>). Every reason is written, 0 included.</item>
/// </list>
/// A shard in none of these lists was run once, and its result is one machine's word
/// (<c>single_execution</c> says how many). Schema <c>corpus-integrity/8</c>: <c>/7</c> with the
/// second dispute round (<c>again</c> in a dispute's record, <c>disputes_again</c> and
/// <c>disputes_again_resolved</c> in the summary, <c>dispute_again</c> in <c>rounds</c>).
/// <c>/7</c> was <c>/6</c> with
/// <c>sampling</c> and <c>rounds</c>, the repair round's pairs beside the first round's (each
/// pair says its <c>round</c>; three more counters in the summary), and <c>repair_copies</c> in
/// a dispute's record, since a shard that goes out again goes out on two machines when the run
/// asked for sampling. <c>/6</c> was the shape of
/// <c>/5</c> with one more fault code (<c>tasks</c>: a node's task package did not arrive) and a
/// wider <c>surplus</c> (an answer beyond the ones a round opens for a package, two results for
/// one shard in one answer), since tasks and results travel in packages; <c>/4</c> let a
/// result opt out of the comparison by the version it claimed and chose the execution that stood
/// for a shard by its own content hash. <c>version_skew</c> is kept for readers of <c>/4</c> and
/// is always 0: a result of another version is dropped (<c>rejected</c>, reason <c>version</c>).
/// </summary>
public static class CorpusIntegrityWriter
{
    #region Constants

    /// <summary>The document schema name.</summary>
    public const string SCHEMA = "corpus-integrity/8";

    /// <summary>A compared pair's signatures: both sides made the same.</summary>
    public const string SIGNATURES_MATCH = "match";

    /// <summary>A compared pair's signatures: the sides made different ones (a mismatch, <c>signatures</c> in its differences).</summary>
    public const string SIGNATURES_DIFFER = "differ";

    /// <summary>A compared pair's signatures: neither side made any (the pipeline does not sign); nothing to compare.</summary>
    public const string SIGNATURES_NONE = "none";

    /// <summary>The bytes the writer holds before it hands them to the stream.</summary>
    private const int FLUSH_BYTES = 1 << 16;

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
        using var stream = new MemoryStream();
        Write(ledger, stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes the ledger's account to a stream. The run is read once for the summary and once for
    /// each of the three lists (pairs, disputes, shards without a result), every record written
    /// as its shard is judged: the lists grow with the run, and none of them is held.
    /// </summary>
    /// <param name="ledger">The server's account of the run.</param>
    /// <param name="stream">Receives the UTF-8 JSON.</param>
    public static void Write(CorpusLedger ledger, Stream stream)
    {
        int shards = 0, sampled = 0, compared = 0, mismatches = 0, signaturesCompared = 0, signatureMismatches = 0, unanswered = 0;
        int repairPairs = 0, repairCompared = 0, repairMismatches = 0;
        foreach (var shard in ledger.Shards)
        {
            shards++;
            if (shard.Outcome == CorpusShardOutcome.NoResult)
                unanswered++;

            // The repair round's pairs are counted apart: they are no sample of the run.
            if (shard.Repair.Count >= 2)
            {
                var again = Pair(shard, shard.Repair, CorpusFanOutWriter.ROUND_REPAIR);
                repairPairs++;
                if (again.Compared)
                    repairCompared++;
                if (again.Compared && !again.Match)
                    repairMismatches++;
            }

            if (shard.First.Count < 2)
                continue;

            var pair = Pair(shard, shard.First, CorpusFanOutWriter.ROUND_FIRST);
            sampled++;
            if (!pair.Compared)
                continue;

            compared++;
            if (!pair.Match)
                mismatches++;
            if (pair.Signatures != SIGNATURES_NONE)
                signaturesCompared++;
            if (pair.Signatures == SIGNATURES_DIFFER)
                signatureMismatches++;
        }

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);
        writer.WriteString("controller", ledger.ControllerVersion);

        writer.WriteStartObject("summary");
        writer.WriteNumber("shards", shards);
        writer.WriteNumber("single_execution", ledger.SingleExecutions);
        writer.WriteNumber("sampled", sampled);
        writer.WriteNumber("compared", compared);
        writer.WriteNumber("version_skew", 0);
        writer.WriteNumber("incomplete", sampled - compared);
        writer.WriteNumber("mismatches", mismatches);
        writer.WriteNumber("mismatch_rate", compared == 0 ? 0.0 : (double)mismatches / compared);
        writer.WriteNumber("signatures_compared", signaturesCompared);
        writer.WriteNumber("signature_mismatches", signatureMismatches);
        writer.WriteNumber("rejected_results", ledger.RejectedResults);
        writer.WriteNumber("node_faults", ledger.NodeFaults);
        writer.WriteNumber("repairs", ledger.Repairs);
        writer.WriteNumber("repaired", ledger.Repaired);
        writer.WriteNumber("repair_pairs", repairPairs);
        writer.WriteNumber("repair_compared", repairCompared);
        writer.WriteNumber("repair_mismatches", repairMismatches);
        writer.WriteNumber("no_result", unanswered);
        writer.WriteNumber("disputes", ledger.Disputes);
        writer.WriteNumber("disputes_resolved", ledger.DisputesResolved);
        writer.WriteNumber("disputed", ledger.Disputes - ledger.DisputesResolved);
        writer.WriteNumber("disputes_again", ledger.DisputesAgain);
        writer.WriteNumber("disputes_again_resolved", ledger.DisputesAgainResolved);
        writer.WriteEndObject();

        // What the run asked for in second executions, and how each round came back.
        CorpusFanOutWriter.Write(writer, ledger);

        writer.WriteStartObject("rejected");
        foreach (var reason in CorpusLedger.REJECTION_REASONS)
            writer.WriteNumber(reason, ledger.Rejected.GetValueOrDefault(reason));
        writer.WriteEndObject();

        writer.WriteStartArray("pairs");
        foreach (var shard in ledger.Shards)
        {
            if (shard.First.Count >= 2)
                WritePair(writer, Pair(shard, shard.First, CorpusFanOutWriter.ROUND_FIRST));

            if (shard.Repair.Count >= 2)
                WritePair(writer, Pair(shard, shard.Repair, CorpusFanOutWriter.ROUND_REPAIR));

            Flush(writer);
        }

        writer.WriteEndArray();

        // A run without a dispute and without a shard left unanswered is not read again for them.
        writer.WriteStartArray("disputes");
        if (ledger.Disputes > 0)
        {
            foreach (var shard in ledger.Shards)
            {
                if (shard.NeedsDispute)
                    WriteDispute(writer, shard);

                Flush(writer);
            }
        }

        writer.WriteEndArray();

        writer.WriteStartArray("no_result");
        if (unanswered > 0)
        {
            foreach (var shard in ledger.Shards)
            {
                if (shard.Outcome == CorpusShardOutcome.NoResult)
                    WriteNoResult(writer, shard);

                Flush(writer);
            }
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>The fields two results of one shard disagree on; empty when they agree.</summary>
    /// <param name="primary">A checked result.</param>
    /// <param name="replica">Another one of the same shard.</param>
    /// <returns>Field names: verdict, content, signatures, documents, drops, languages, pdf.</returns>
    public static List<string> Differences(CorpusResultData primary, CorpusResultData replica)
    {
        return CorpusLedger.Differences(primary, replica);
    }

    /// <summary>Hands what is written to the stream once it is more than a buffer's worth.</summary>
    private static void Flush(Utf8JsonWriter writer)
    {
        if (writer.BytesPending > FLUSH_BYTES)
            writer.Flush();
    }

    /// <summary>The first two values of a shard in one round, side by side.</summary>
    private static PairRecord Pair(CorpusLedgerShard shard, List<CorpusLedgerEntry> entries, string round)
    {
        var primary = entries[0];
        var replica = entries[1];
        var compared = CorpusLedger.IsCompared(entries);
        var differing = compared ? CorpusLedger.Differences(primary.Result!, replica.Result!) : [];

        var signatures = !compared ? SIGNATURES_NONE
            : primary.Result!.SignaturesSha256.Length == 0 && replica.Result!.SignaturesSha256.Length == 0 ? SIGNATURES_NONE
            : CorpusLedger.SignaturesDiffer(differing) ? SIGNATURES_DIFFER
            : SIGNATURES_MATCH;

        return new PairRecord(shard, round, entries.Count, primary, replica, compared, differing.Count == 0, differing, signatures);
    }

    private static void WritePair(Utf8JsonWriter writer, PairRecord pair)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", pair.Shard.Index);
        writer.WriteString("round", pair.Round);
        WriteSide(writer, "primary", pair.Primary);
        WriteSide(writer, "replica", pair.Replica);
        if (pair.Values > 2)
            writer.WriteNumber("results", pair.Values);
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

        // The pair a dispute came from: the repair round's when the shard went out again, the first round's otherwise.
        if (pair.Shard.NeedsDispute && pair.Shard.SentToRepair == (pair.Round == CorpusFanOutWriter.ROUND_REPAIR))
            writer.WriteBoolean("dispute", true);

        writer.WriteEndObject();
    }

    private static void WriteDispute(Utf8JsonWriter writer, CorpusLedgerShard shard)
    {
        var resolved = shard.Outcome == CorpusShardOutcome.Accepted;

        writer.WriteStartObject();
        writer.WriteNumber("index", shard.Index);
        writer.WriteNumber("first_copies", shard.First.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
        writer.WriteNumber("repair_copies", shard.Repair.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
        writer.WriteNumber("copies", shard.Dispute.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
        writer.WriteNumber("agreeing", shard.DisputeAgreeing);
        writer.WriteString("outcome", shard.DisputeOutcome);
        writer.WriteStartArray("results");
        foreach (var entry in shard.Dispute)
            WriteSide(writer, null, entry);
        writer.WriteEndArray();

        // The second dispute round, for a dispute the first left with too few copies.
        if (shard.SentToDisputeAgain && shard.AgainAfter.Length > 0)
        {
            writer.WriteStartObject("again");
            writer.WriteString("after", shard.AgainAfter);
            writer.WriteNumber("copies", shard.Again.Count(entry => entry.Kind == CorpusLedgerEntryKind.Execution));
            writer.WriteNumber("agreeing", shard.AgainAgreeing);
            writer.WriteStartArray("results");
            foreach (var entry in shard.Again)
                WriteSide(writer, null, entry);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

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

    /// <summary>The first two results of a shard that came back more than once in a round, and how many values the round brought it: compared only when both are results with a verdict.</summary>
    private sealed record PairRecord(CorpusLedgerShard Shard, string Round, int Values, CorpusLedgerEntry Primary, CorpusLedgerEntry Replica, bool Compared, bool Match, List<string> Differing, string Signatures);

    #endregion
}
