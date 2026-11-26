using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// Writes the integrity document of a run (<c>docs/smt-integrity.schema.json</c>): what the host
/// did with what the nodes returned, in counts. The copies made, how they compared, the disputes
/// and how they ended, the answers a lie was found in, the problems on which the evaluator and the
/// solver disagree (loudly: each by its index), the repairs and why, the problems left without a
/// result, the result packages refused and the packages nodes could not work on, what every
/// fan-out was asked for and what came back, and whether the platform told the host which package
/// each result was returned for. It names no machine and no package.
/// </summary>
internal static class SmtIntegrityWriter
{
    #region Constants

    /// <summary>The schema's name and version.</summary>
    public const string SCHEMA = "smt-integrity/1";

    private const int FLUSH_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>Writes the document.</summary>
    /// <param name="stream">Where.</param>
    /// <param name="facts">What the settlement counted.</param>
    public static void Write(Stream stream, SmtIntegrityFacts facts)
    {
        var resolved = facts.Disputes.Count(dispute => dispute.Resolved);

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);

        writer.WriteStartObject("summary");
        writer.WriteNumber("problems", facts.Problems);
        writer.WriteNumber("copies_made", facts.CopiesMade);
        writer.WriteNumber("compared", facts.Compared);
        writer.WriteNumber("agreements", facts.Agreements);
        writer.WriteNumber("disagreements", facts.Disagreements);
        writer.WriteNumber("one_copy", facts.OneCopy);
        writer.WriteNumber("unsat_one_copy", facts.UnsatOneCopy);
        writer.WriteNumber("complete_one_copy", facts.CompleteOneCopy);
        writer.WriteNumber("disputes", facts.Disputes.Count);
        writer.WriteNumber("disputes_resolved", resolved);
        writer.WriteNumber("disputed", facts.Disputes.Count - resolved);
        WriteCounts(writer, "dispute_causes", SmtRunWords.DISPUTE_CAUSES, facts.Disputes.GroupBy(dispute => dispute.Cause).ToDictionary(group => group.Key, group => group.Count()));
        WriteCounts(writer, "dispute_outcomes", SmtRunWords.DISPUTE_OUTCOMES, facts.Disputes.GroupBy(dispute => dispute.Outcome).ToDictionary(group => group.Key, group => group.Count()));
        writer.WriteNumber("lies", facts.Lies);
        writer.WriteNumber("problems_with_lies", facts.ProblemsWithLies);
        writer.WriteNumber("failed_models", facts.FailedModels);
        writer.WriteNumber("evaluator_solver_disagreements", facts.EvaluatorDisagreements.Count);
        writer.WriteNumber("repairs", facts.Repairs);
        WriteOpenCounts(writer, "repairs_by_reason", facts.RepairReasons);
        writer.WriteNumber("no_result", facts.NoResult);
        WriteOpenCounts(writer, "no_result_by_reason", facts.NoResultReasons);
        writer.WriteNumber("rejected_results", facts.Rejected.Values.Sum());
        WriteCounts(writer, "rejected", SmtRoundIntake.REASONS, facts.Rejected);
        writer.WriteNumber("node_faults", facts.Faults.Values.Sum());
        WriteCounts(writer, "node_faults_by_code", SmtNodeFaults.ALL, facts.Faults);
        SmtSampling.Write(writer, facts.SamplingRate, facts.Sampling, facts.UnansweredPackages);
        writer.WriteString("binding", facts.Binding);
        writer.WriteNumber("results_bound", facts.BoundResults);
        writer.WriteNumber("results_unbound", facts.UnboundResults);
        writer.WriteEndObject();

        // Loudly, by index: the problems on which the machines agree and the evaluator does not.
        writer.WriteStartArray("evaluator_solver_disagreements");
        foreach (var (index, reason, detail) in facts.EvaluatorDisagreements)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", index);
            writer.WriteString("reason", reason);
            writer.WriteString("detail", detail);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("fan_outs");
        foreach (var fanOut in facts.FanOuts)
        {
            writer.WriteStartObject();
            writer.WriteString("round", fanOut.Round);
            writer.WriteNumber("packages", fanOut.Packages);
            writer.WriteNumber("problems", fanOut.Problems);
            writer.WriteNumber("copies", fanOut.Copies);
            if (fanOut.Selected is { } selected)
                writer.WriteNumber("selected", selected);
            else
                writer.WriteNull("selected");
            writer.WriteNumber("complete", fanOut.Complete);
            writer.WriteNumber("unanswered", fanOut.Unanswered);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("disputes");
        foreach (var dispute in facts.Disputes)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", dispute.Index);
            writer.WriteString("cause", dispute.Cause);
            writer.WriteNumber("first_copies", dispute.FirstCopies);
            writer.WriteNumber("copies", dispute.Copies);
            writer.WriteNumber("agreeing", dispute.Agreeing);
            writer.WriteString("outcome", dispute.Outcome);
            writer.WriteEndObject();

            if (writer.BytesPending >= FLUSH_BYTES)
                writer.Flush();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    #endregion

    #region Tools

    private static void WriteCounts(Utf8JsonWriter writer, string name, IReadOnlyList<string> words, IReadOnlyDictionary<string, int> counts)
    {
        writer.WriteStartObject(name);
        foreach (var word in words)
            writer.WriteNumber(word, counts.GetValueOrDefault(word));
        writer.WriteEndObject();
    }

    private static void WriteOpenCounts(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, int> counts)
    {
        writer.WriteStartObject(name);
        foreach (var (key, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteNumber(key, count);
        writer.WriteEndObject();
    }

    #endregion
}
