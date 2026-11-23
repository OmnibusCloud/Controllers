using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// Writes the report of a run (<c>docs/smt-report.schema.json</c>): what it ran with in short,
/// the problem set as it was read, the verdicts and the unknowns by reason, the agreement with the
/// expected verdicts, the evaluator's classes by logic, the copies and how the disputes ended, the
/// problems answered <c>unsat</c> by one machine only, the long tail with the budget, the
/// attribution from the problems' <c>:source</c> and <c>:license</c>, and for enumerations the
/// problems with exactly one model and those with two or more. It names no machine.
/// </summary>
internal static class SmtReportWriter
{
    #region Constants

    /// <summary>The schema's name and version.</summary>
    public const string SCHEMA = "smt-report/1";

    private const int FLUSH_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>Writes the report.</summary>
    /// <param name="stream">Where.</param>
    /// <param name="provenance">What the run ran with.</param>
    /// <param name="set">The problem set as it was read.</param>
    /// <param name="facts">What was counted.</param>
    /// <param name="sampling">The state of the sampling, one of <see cref="SmtSampling.STATES"/>.</param>
    /// <param name="unanswered">The packages of problems no taken answer came back for, every fan-out together.</param>
    public static void Write(Stream stream, SmtRunProvenance provenance, SmtProblemSetSummary set, SmtReportFacts facts, string sampling, int unanswered)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", SCHEMA);

        writer.WriteStartObject("run");
        writer.WriteString("profile", SmtProfile.ID);
        writer.WriteString("module_sha256", SmtProfile.MODULE_SHA256);
        writer.WriteString("controller", ControllerBuildInfo.VERSION);
        SmtSampling.Write(writer, provenance.SamplingRate, sampling, unanswered);
        provenance.WriteOptions(writer);
        writer.WriteEndObject();

        WriteSet(writer, set);

        WriteCounts(writer, "statuses", SmtRunWords.STATUSES, facts.Statuses);
        WriteCounts(writer, "verdicts", [SmtResultWords.SAT, SmtResultWords.UNSAT, SmtResultWords.UNKNOWN], facts.Verdicts);
        WriteCounts(writer, "unknown", [SmtResultWords.RLIMIT, SmtResultWords.FUEL, SmtResultWords.MEMOUT, SmtResultWords.INCOMPLETE, SmtResultWords.OTHER], facts.Unknowns);
        WriteCounts(writer, "expected", SmtRunWords.EXPECTATIONS, facts.Expectations);

        writer.WriteStartObject("validation");
        WriteCounts(writer, "classes", [SmtRunWords.VALIDATED, SmtRunWords.PARTIAL, SmtRunWords.FAILED], facts.ClassTotals);
        writer.WriteStartObject("by_logic");
        foreach (var (logic, counts) in facts.Classes)
            WriteCounts(writer, logic, [SmtRunWords.VALIDATED, SmtRunWords.PARTIAL, SmtRunWords.FAILED], counts);
        writer.WriteEndObject();
        WriteOpenCounts(writer, "partial_by_reason", facts.PartialReasons);
        writer.WriteEndObject();

        writer.WriteStartObject("copies");
        writer.WriteNumber("two_or_more", facts.TwoOrMoreCopies);
        writer.WriteNumber("agreed", facts.AgreedCopies);
        writer.WriteNumber("disputed", facts.DisputeOutcomes.Values.Sum());
        WriteCounts(writer, "disputes", SmtRunWords.DISPUTE_OUTCOMES, facts.DisputeOutcomes);
        WriteCounts(writer, "settled_by", SmtRunWords.SETTLED_BY, facts.SettledBy);
        writer.WriteEndObject();

        writer.WriteStartObject("unsat_one_copy");
        writer.WriteNumber("count", facts.UnsatOneCopy.Count);
        WriteIndices(writer, "indices", facts.UnsatOneCopy);
        writer.WriteEndObject();

        WriteTail(writer, provenance, facts);
        WriteAttribution(writer, facts);

        if (facts.HasEnumerations)
        {
            writer.WriteStartObject("enumerate");
            writer.WriteNumber("exactly_one", facts.ExactlyOne.Count);
            writer.WriteNumber("two_or_more", facts.TwoOrMore.Count);
            writer.WriteNumber("no_model", facts.NoModel);
            writer.WriteNumber("undecided", facts.Undecided);
            WriteIndices(writer, "kept", facts.ExactlyOne);
            WriteIndices(writer, "dropped", facts.TwoOrMore);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    #endregion

    #region Tools

    private static void WriteSet(Utf8JsonWriter writer, SmtProblemSetSummary set)
    {
        writer.WriteStartObject("problem_set");
        writer.WriteNumber("rows", set.Rows);
        writer.WriteNumber("accepted", set.Accepted);
        writer.WriteNumber("refused", set.Refused);
        WriteOpenCounts(writer, "refused_by_code", set.RefusalCodes);
        WriteOpenCounts(writer, "logics", set.Logics);
        writer.WriteNumber("other_logics", set.OtherLogics);
        WriteOpenCounts(writer, "queries", set.Queries.ToDictionary(pair => SmtResultsWriter.Query(pair.Key), pair => pair.Value));
        WriteOpenCounts(writer, "expected", set.Expected.ToDictionary(pair => SmtResultsWriter.Expected(pair.Key), pair => pair.Value));
        writer.WriteEndObject();
    }

    private static void WriteTail(Utf8JsonWriter writer, SmtRunProvenance provenance, SmtReportFacts facts)
    {
        var envelope = SmtProfile.Envelope(provenance.Options);

        writer.WriteStartObject("long_tail");
        writer.WriteNumber("rlimit", envelope.ResourceLimit);
        writer.WriteNumber("fuel", envelope.Fuel);
        writer.WriteStartArray("problems");
        foreach (var entry in facts.SortedTail())
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", entry.Index);
            writer.WriteNumber("resource_count", entry.ResourceCount);
            writer.WriteNumber("fuel", entry.Fuel);
            writer.WriteString("stopped_by", entry.StoppedBy);
            writer.WriteEndObject();

            if (writer.BytesPending >= FLUSH_BYTES)
                writer.Flush();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteAttribution(Utf8JsonWriter writer, SmtReportFacts facts)
    {
        writer.WriteStartObject("attribution");
        writer.WriteStartArray("sources");
        foreach (var ((source, license), count) in facts.Attributions.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.Source, StringComparer.Ordinal).ThenBy(pair => pair.Key.License, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("source", source);
            writer.WriteString("license", license);
            writer.WriteNumber("problems", count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("other_problems", facts.OtherAttributed);
        writer.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter writer, string name, IReadOnlyList<string> words, IReadOnlyDictionary<string, int> counts)
    {
        writer.WriteStartObject(name);
        foreach (var word in words)
            writer.WriteNumber(word, counts.GetValueOrDefault(word));
        writer.WriteEndObject();
    }

    private static void WriteOpenCounts(Utf8JsonWriter writer, string name, IEnumerable<KeyValuePair<string, int>> counts)
    {
        writer.WriteStartObject(name);
        foreach (var (key, count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteNumber(key, count);
        writer.WriteEndObject();
    }

    private static void WriteIndices(Utf8JsonWriter writer, string name, IEnumerable<int> indices)
    {
        writer.WriteStartArray(name);
        foreach (var index in indices.Order())
        {
            writer.WriteNumberValue(index);
            if (writer.BytesPending >= FLUSH_BYTES)
                writer.Flush();
        }

        writer.WriteEndArray();
    }

    #endregion
}
