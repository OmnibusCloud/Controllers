using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the integrity document of a run: what the server did with what the nodes returned.
/// Every task executed more than once in the first fan-out, its executions paired and whether
/// they agree on everything an honest node cannot vary (verdict, exit code, outputs, per-case
/// outcomes); every dispute and how three more executions settled it; and the counts: the result
/// packages refused and why, the tasks sent out once more, the tasks left without a result, and
/// how many tasks rest on one execution because they were not sampled. The platform keeps the
/// executions of one batch on different machines, and the document does not say which.
/// </summary>
internal static class VerifyIntegrityWriter
{
    #region Constants

    /// <summary>The document's schema tag.</summary>
    public const string SCHEMA = "verify-integrity/2";

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the facts of a settled run as UTF-8 JSON.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(VerifyIntegrityFacts facts)
    {
        var mismatches = facts.Pairs.Count(pair => pair.Differs.Count > 0);
        var resolved = facts.Disputes.Count(dispute => dispute.Resolved);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);

            writer.WriteStartObject("summary");
            writer.WriteNumber("tasks", facts.Tasks);
            writer.WriteNumber("sampled", facts.Pairs.Count);
            writer.WriteNumber("compared", facts.Pairs.Count);
            writer.WriteNumber("mismatches", mismatches);
            writer.WriteNumber("mismatch_rate", facts.Pairs.Count == 0 ? 0.0 : (double)mismatches / facts.Pairs.Count);
            writer.WriteNumber("agreed", facts.Agreed);
            writer.WriteNumber("single_execution", facts.SingleExecution);
            writer.WriteNumber("disputes", facts.Disputes.Count);
            writer.WriteNumber("disputes_resolved", resolved);
            writer.WriteNumber("disputed", facts.Disputes.Count - resolved);
            writer.WriteNumber("repairs", facts.Repairs);
            writer.WriteNumber("no_result", facts.NoResult);
            writer.WriteNumber("rejected_results", facts.Rejected.Values.Sum());
            writer.WriteStartObject("rejected");
            foreach (var reason in VerifyRoundIntake.REASONS)
                writer.WriteNumber(reason, facts.Rejected.GetValueOrDefault(reason));
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartArray("pairs");
            foreach (var pair in facts.Pairs)
                WritePair(writer, pair);
            writer.WriteEndArray();

            writer.WriteStartArray("disputes");
            foreach (var dispute in facts.Disputes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", dispute.Index);
                writer.WriteNumber("first_copies", dispute.FirstCopies);
                writer.WriteNumber("copies", dispute.Copies);
                writer.WriteNumber("agreeing", dispute.Agreeing);
                writer.WriteString("outcome", dispute.Outcome);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WritePair(Utf8JsonWriter writer, VerifyPairRecord pair)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", pair.Index);
        WriteSide(writer, "primary", pair.Primary);
        WriteSide(writer, "replica", pair.Replica);
        writer.WriteBoolean("compared", true);
        writer.WriteBoolean("match", pair.Differs.Count == 0);
        if (pair.Differs.Count > 0)
        {
            writer.WriteStartArray("differs");
            foreach (var field in pair.Differs)
                writer.WriteStringValue(field);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteSide(Utf8JsonWriter writer, string name, VerifyExecution execution)
    {
        writer.WriteStartObject(name);
        writer.WriteString("verdict", execution.Verdict.ToString());
        writer.WriteNumber("exit", execution.ExitCode);
        writer.WriteEndObject();
    }

    #endregion
}
