using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// Writes the results of a run (<c>docs/smt-results.schema.json</c>): one JSON line a row of the
/// problem set, in the set's order, refused rows included. A row says how its problem was settled
/// and what the answer is, in the words of a row; it names no machine: how many copies answered
/// and whether they agreed, never which. What a lab sees of the models is cut at
/// <see cref="MAX_MODEL_BYTES"/> a row (whole texts only, in the order the session gave them, and
/// <c>model_truncated</c> says so); the full models of such a row go to the ledger's own section.
/// </summary>
internal static class SmtResultsWriter
{
    #region Constants

    /// <summary>The most bytes of models and values (UTF-8) a row of the results shows.</summary>
    public const int MAX_MODEL_BYTES = 64 * 1024;

    private const byte LINE_FEED = (byte)'\n';

    private static readonly JsonWriterOptions OPTIONS = new() { Indented = false };

    #endregion

    #region Functions

    /// <summary>Writes the row of a refused row of the set.</summary>
    /// <param name="stream">The results.</param>
    /// <param name="index">The row's index in the set.</param>
    /// <param name="refusal">Why it was refused.</param>
    public static void WriteRefused(Stream stream, int index, SmtRefusalData refusal)
    {
        using (var writer = new Utf8JsonWriter(stream, OPTIONS))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", index);

            // The id of a refused row only when it is one an id may be: a row refused for its id
            // keeps none here.
            writer.WriteString("id", refusal.File.Length <= SmtProblemSetLimits.MAX_ID_CHARACTERS ? refusal.File : "");
            writer.WriteString("status", SmtRunWords.REFUSED);
            writer.WriteString("code", refusal.Code);
            writer.WriteString("message", refusal.Reason);
            writer.WriteNumber("line", refusal.Line);
            writer.WriteString("verdict", SmtResultWords.NONE);
            writer.WriteString("reason", SmtResultWords.NONE);
            writer.WriteString("expected", SmtResultWords.NONE);
            writer.WriteString("expected_agreement", SmtRunWords.NOT_APPLICABLE);
            WriteValidation(writer, SmtModelCheck.NONE);
            writer.WriteNumber("copies", 0);
            writer.WriteBoolean("agreed", false);
            writer.WriteString("settled_by", "");
            writer.WriteBoolean("model_truncated", false);
            writer.WriteEndObject();
        }

        stream.WriteByte(LINE_FEED);
    }

    /// <summary>Writes the row of a taken problem; when its models were cut, writes them whole to the ledger's section of models.</summary>
    /// <param name="stream">The results.</param>
    /// <param name="models">The ledger's section of full models.</param>
    /// <param name="data">The problem as it travelled.</param>
    /// <param name="problem">The problem as the host's sanitizer took it.</param>
    /// <param name="ruling">How it was settled.</param>
    /// <param name="row">The settled row, or null when it has none.</param>
    /// <returns>Whether the row's models were cut.</returns>
    public static bool WriteTaken(Stream stream, Stream models, SmtProblemData data, SmtProblem problem, SmtRuling ruling, SmtResultData? row)
    {
        bool truncated;
        using (var writer = new Utf8JsonWriter(stream, OPTIONS))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", data.Index);
            writer.WriteString("id", data.Id);
            writer.WriteString("query", Query(data.Query));
            writer.WriteString("logic", problem.Logic ?? "");
            writer.WriteString("status", row?.Status ?? ruling.HostStatus);
            writer.WriteString("code", row?.Code ?? ruling.Reason);
            writer.WriteString("message", row?.Message ?? "");

            var verdict = row?.Verdict ?? SmtResultWords.NONE;
            writer.WriteString("verdict", verdict);
            writer.WriteString("reason", row?.Reason ?? SmtResultWords.NONE);
            writer.WriteString("expected", Expected(data.Expected));
            writer.WriteString("expected_agreement", Agreement(data, row));

            if (data.Query == SmtQuery.Enumerate)
            {
                writer.WriteNumber("count", row?.Count ?? 0);
                writer.WriteBoolean("complete", row?.Complete ?? false);
                writer.WriteBoolean("limit_reached", row?.LimitReached ?? false);
            }

            if (data.Query == SmtQuery.Prove)
                writer.WriteString("proof", row?.Proof ?? SmtResultWords.NONE);

            truncated = WriteModels(writer, data.Query, row);
            writer.WriteBoolean("model_truncated", truncated);

            WriteValidation(writer, ruling.Check);
            writer.WriteNumber("copies", ruling.Copies);
            writer.WriteBoolean("agreed", ruling.Agreed);
            writer.WriteString("settled_by", ruling.SettledBy);

            if (row != null)
            {
                writer.WriteString("digest", row.Digest);
                writer.WriteStartObject("stats");
                writer.WriteNumber("resource_count", row.ResourceCount);
                writer.WriteNumber("fuel", row.Fuel);
                writer.WriteNumber("checks", row.Checks);
                writer.WriteEndObject();
            }

            if (!string.IsNullOrEmpty(data.Tags))
                writer.WriteString("tags", data.Tags);

            writer.WriteEndObject();
        }

        stream.WriteByte(LINE_FEED);

        if (truncated && row != null)
            WriteFullModels(models, data.Index, row);

        return truncated;
    }

    /// <summary>The word of a query, as a problem set names it.</summary>
    /// <param name="query">The query.</param>
    /// <returns><c>check</c>, <c>enumerate</c> or <c>prove</c>.</returns>
    public static string Query(SmtQuery query)
    {
        return query switch
        {
            SmtQuery.Enumerate => "enumerate",
            SmtQuery.Prove => "prove",
            _ => "check"
        };
    }

    /// <summary>The word of an expected verdict.</summary>
    /// <param name="expected">The expected verdict.</param>
    /// <returns><c>sat</c>, <c>unsat</c> or <c>none</c>.</returns>
    public static string Expected(SmtProblemStatus expected)
    {
        return expected switch
        {
            SmtProblemStatus.Sat => SmtResultWords.SAT,
            SmtProblemStatus.Unsat => SmtResultWords.UNSAT,
            _ => SmtResultWords.NONE
        };
    }

    /// <summary>
    /// How a row's verdict stands against the verdict its problem expects: <c>agree</c>,
    /// <c>disagree</c>, or <c>n/a</c> when nothing is expected, the query is a proof, or the row
    /// decides nothing (no row, or a verdict that is neither sat nor unsat).
    /// </summary>
    /// <param name="data">The problem.</param>
    /// <param name="row">Its settled row, or null.</param>
    /// <returns>One of <see cref="SmtRunWords.EXPECTATIONS"/>.</returns>
    public static string Agreement(SmtProblemData data, SmtResultData? row)
    {
        if (row == null || data.Query == SmtQuery.Prove || data.Expected == SmtProblemStatus.None)
            return SmtRunWords.NOT_APPLICABLE;

        if (row.Verdict is not (SmtResultWords.SAT or SmtResultWords.UNSAT))
            return SmtRunWords.NOT_APPLICABLE;

        return row.Verdict == Expected(data.Expected) ? SmtRunWords.AGREE : SmtRunWords.DISAGREE;
    }

    #endregion

    #region Tools

    private static void WriteValidation(Utf8JsonWriter writer, SmtModelCheck check)
    {
        writer.WriteString("validation", check.Class);
        writer.WriteString("validation_reason", check.Reason);
        if (check.Detail.Length > 0)
            writer.WriteString("validation_detail", check.Detail);
    }

    /// <summary>
    /// Writes the texts of a row's models and values, whole, in the order the session gave them,
    /// while they fit the budget of a row; the first that does not fit and every one after it are
    /// written as null.
    /// </summary>
    private static bool WriteModels(Utf8JsonWriter writer, SmtQuery query, SmtResultData? row)
    {
        var models = row?.Models ?? [];
        var values = row?.Values ?? [];
        var budget = new SmtResultsBudget();

        switch (query)
        {
            case SmtQuery.Enumerate:
                writer.WriteStartArray("models");
                for (var model = 0; model < models.Count; model++)
                {
                    writer.WriteStartObject();
                    WriteText(writer, "values", model < values.Count ? values[model] : null, budget);
                    WriteText(writer, "model", models[model], budget);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case SmtQuery.Check:
                WriteText(writer, "values", values.Count > 0 ? values[0] : null, budget);
                WriteText(writer, "model", models.Count > 0 ? models[0] : null, budget);
                break;
            default:
                WriteText(writer, "model", models.Count > 0 ? models[0] : null, budget);
                break;
        }

        return budget.Cut;
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? text, SmtResultsBudget budget)
    {
        if (text == null)
        {
            writer.WriteNull(name);
            return;
        }

        if (budget.Cut || budget.Spent + Encoding.UTF8.GetByteCount(text) > MAX_MODEL_BYTES)
        {
            budget.Cut = true;
            writer.WriteNull(name);
            return;
        }

        budget.Spent += Encoding.UTF8.GetByteCount(text);
        writer.WriteString(name, text);
    }

    private static void WriteFullModels(Stream models, int index, SmtResultData row)
    {
        using (var writer = new Utf8JsonWriter(models, OPTIONS))
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", index);
            writer.WriteStartArray("models");
            foreach (var model in row.Models)
                writer.WriteStringValue(model);
            writer.WriteEndArray();
            writer.WriteStartArray("values");
            foreach (var value in row.Values)
                writer.WriteStringValue(value);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        models.WriteByte(LINE_FEED);
    }

    #endregion
}
