using System.Text.Json;
using static OutWit.Controller.AI.Smt.Live.Documents.SmtLiveJson;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// Prints what a run's report and integrity document say, with the rows of the results a lab
/// must look at: what the run ran with, the set, the verdicts and the unknowns by reason, the
/// agreement with the expected verdicts, the evaluator's classes, the copies and how they
/// agreed, what rests on one machine and whether the platform told the server which package
/// each result came back for, the disputes and how they ended, the lies found, the
/// disagreements between the evaluator and the solver, the problems without a result, and how
/// the work was delivered. The report is held to the rows it was written from. What a
/// known-answers run must not have is said loudly, and returned as the run's alarms: every loud
/// line is an alarm, and every alarm has a loud line, whether the report or a row says it.
/// </summary>
internal sealed class SmtLiveRunSummary
{
    #region Constants

    /// <summary>The prefix of a line a lab must not miss.</summary>
    public const string LOUD = "!!! ";

    private const int SHOWN_ROWS = 10;
    private const int SHOWN_INDICES = 20;
    private const int SHOWN_DIFFERENCES = 10;

    // The report's word for a sampling that was asked for and did not happen.
    private const string SHORT = "short";

    #endregion

    #region Fields

    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes a printer.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    public SmtLiveRunSummary(TextWriter output)
    {
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Prints the summary of a run's documents.
    /// </summary>
    /// <param name="reportPath">The report.</param>
    /// <param name="integrityPath">The integrity document.</param>
    /// <param name="resultsPath">The results.</param>
    /// <returns>The alarms: what the run reports that a run on an honest fleet must not; empty when there is none.</returns>
    /// <exception cref="JsonException">The report or the integrity document is not JSON.</exception>
    public IReadOnlyList<string> Print(string reportPath, string integrityPath, string resultsPath)
    {
        using var reportDocument = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        using var integrityDocument = JsonDocument.Parse(File.ReadAllBytes(integrityPath));
        var report = reportDocument.RootElement;
        var integrity = integrityDocument.RootElement;
        var summary = At(integrity, "summary");
        var rows = SmtLiveRowScan.Scan(resultsPath, Listed(integrity), SHOWN_ROWS);

        PrintRun(report);
        PrintAnswers(report);
        PrintCopies(report, summary);
        PrintIntegrity(integrity, summary, rows.Ids);
        PrintRows(rows);

        var differences = Differences(report, rows);
        if (differences.Count > 0)
            Line("rows", $"THE REPORT AND THE RESULTS DISAGREE: {string.Join("; ", differences.Take(SHOWN_DIFFERENCES))}" +
                         $"{(differences.Count > SHOWN_DIFFERENCES ? $" and {differences.Count - SHOWN_DIFFERENCES} more" : "")}", true);

        return Alarms(report, summary, rows, differences.Count);
    }

    private void PrintRun(JsonElement report)
    {
        var options = At(report, "run", "options");
        Line("run", $"profile {Text(report, "run", "profile")}, module {Head(Text(report, "run", "module_sha256"))}, controller {Text(report, "run", "controller")}");
        Line("options", $"query {Text(options, "query")}, rlimit {Number(options, "rlimit")}, hard cap {Number(options, "hard_cap_s")} s (fuel {Number(options, "fuel")}), " +
                        $"memory {Number(options, "memory_mb")} MB, seed {Number(options, "seed")}, max models {Number(options, "max_models")}");
        Line("sampling", $"rate {Text(report, "run", "sampling_rate")}, {Text(report, "run", "sampling")}; packages without an answer {Number(report, "run", "unanswered_packages")}",
            Text(report, "run", "sampling") == SHORT);

        var set = At(report, "problem_set");
        Line("set", $"{Number(set, "rows")} row(s): {Number(set, "accepted")} taken, {Number(set, "refused")} refused ({NonZero(set, "refused_by_code")})");
        Line("", $"logics: {Counts(set, "logics")}{(Number(set, "other_logics") > 0 ? $", {Number(set, "other_logics")} more" : "")}");
        Line("", $"queries: {Counts(set, "queries")}; expected: {Counts(set, "expected")}");
    }

    private void PrintAnswers(JsonElement report)
    {
        Line("statuses", Counts(report, "statuses"));
        Line("verdicts", Counts(report, "verdicts"));
        Line("unknown", Counts(report, "unknown"));
        Line("expected", Counts(report, "expected"), Number(report, "expected", "disagree") > 0);
        Line("validation", Counts(report, "validation", "classes"), Number(report, "validation", "classes", "failed") > 0);

        var byLogic = At(report, "validation", "by_logic");
        if (byLogic.ValueKind == JsonValueKind.Object)
        {
            foreach (var logic in byLogic.EnumerateObject())
                Line("", $"{(logic.Name.Length == 0 ? "(none)" : logic.Name),-10} validated {Number(logic.Value, "validated")}, partial {Number(logic.Value, "partial")}, failed {Number(logic.Value, "failed")}");
        }

        Line("", $"partial by reason: {Counts(report, "validation", "partial_by_reason")}");

        var enumerate = At(report, "enumerate");
        if (enumerate.ValueKind == JsonValueKind.Object)
        {
            Line("enumerate", $"exactly one {Number(enumerate, "exactly_one")}, two or more {Number(enumerate, "two_or_more")}, " +
                              $"no model {Number(enumerate, "no_model")}, undecided {Number(enumerate, "undecided")}; kept {Indices(enumerate, "kept")}; dropped {Indices(enumerate, "dropped")}");
        }
    }

    private void PrintCopies(JsonElement report, JsonElement summary)
    {
        var copies = At(report, "copies");
        Line("copies", $"{Number(copies, "two_or_more")} problem(s) answered by two machines or more, {Number(copies, "agreed")} of them agreed; " +
                       $"settled by {Counts(copies, "settled_by")}");
        Line("", $"compared {Number(summary, "compared")}: {Number(summary, "agreements")} agreement(s), {Number(summary, "disagreements")} disagreement(s); " +
                 $"{Number(summary, "copies_made")} answer(s) taken in all");
        Line("one copy", $"{Number(summary, "one_copy")} problem(s) rest on one machine, {Number(summary, "unsat_one_copy")} of them unsat ({Indices(report, "unsat_one_copy", "indices")}), " +
                         $"{Number(report, "complete_one_copy", "count")} complete enumeration(s) that found models ({Indices(report, "complete_one_copy", "indices")})");
        Line("binding", $"{Text(summary, "binding")}: {Number(summary, "results_bound")} result(s) told the package they came back for, {Number(summary, "results_unbound")} not");
    }

    private void PrintIntegrity(JsonElement integrity, JsonElement summary, IReadOnlyDictionary<int, string> ids)
    {
        var disputed = Number(summary, "disputed");
        Line("disputes", $"{Number(summary, "disputes")}, resolved {Number(summary, "disputes_resolved")}, standing {disputed}; " +
                         $"causes: {Counts(summary, "dispute_causes")}; outcomes: {Counts(summary, "dispute_outcomes")}", disputed > 0);
        foreach (var dispute in Items(integrity, "disputes").Take(SHOWN_ROWS))
        {
            var index = (int)Number(dispute, "index");
            Line("", $"[{index}] {ids.GetValueOrDefault(index, "")}: {Text(dispute, "cause")}, {Number(dispute, "first_copies")} first copies, " +
                     $"{Number(dispute, "copies")} in the dispute, {Number(dispute, "agreeing")} agreeing: {Text(dispute, "outcome")}");
        }

        var lies = Number(summary, "lies");
        Line("lies", $"{lies} answer(s) in {Number(summary, "problems_with_lies")} problem(s) differ from the settled answer of the others; " +
                     $"models the evaluator called failed {Number(summary, "failed_models")}", lies > 0);

        var disagreements = Number(summary, "evaluator_solver_disagreements");
        Line("evaluator", disagreements == 0
            ? "0 disagreements between the evaluator and the solver"
            : $"EVALUATOR/SOLVER DISAGREEMENTS: {disagreements} problem(s) the machines agree on and the evaluator calls failed", disagreements > 0);
        foreach (var disagreement in Items(integrity, "evaluator_solver_disagreements"))
        {
            var index = (int)Number(disagreement, "index");
            Line("", $"[{index}] {ids.GetValueOrDefault(index, "")}: {Text(disagreement, "reason")} {Text(disagreement, "detail")}", true);
        }

        var noResult = Number(summary, "no_result");
        Line("no result", $"{noResult} problem(s) ({NonZero(summary, "no_result_by_reason")})", noResult > 0);
        Line("delivery", $"repairs {Number(summary, "repairs")} ({NonZero(summary, "repairs_by_reason")}); " +
                         $"refused result packages {Number(summary, "rejected_results")} ({NonZero(summary, "rejected")}); " +
                         $"node faults {Number(summary, "node_faults")} ({NonZero(summary, "node_faults_by_code")})");

        foreach (var fanOut in Items(integrity, "fan_outs"))
        {
            var selected = At(fanOut, "selected");
            Line("fan-out", $"{Text(fanOut, "round")}: {Number(fanOut, "packages")} package(s) of {Number(fanOut, "problems")} problem(s), " +
                            $"{(selected.ValueKind == JsonValueKind.Number ? selected.GetRawText() : "an unstated number")} asked for {Number(fanOut, "copies")} answer(s), " +
                            $"{Number(fanOut, "complete")} complete, {Number(fanOut, "unanswered")} unanswered");
        }
    }

    private void PrintRows(SmtLiveRowScan rows)
    {
        Line("rows", $"{rows.Rows} read; not ok {rows.NotOkCount}{(rows.NotOkCount == 0 ? "" : $" ({string.Join(", ", rows.NotOkByStatus.Select(pair => $"{pair.Key} {pair.Value}"))})")}");
        foreach (var row in rows.NotOk)
            Line("", row);

        if (rows.DisagreeingCount > 0)
        {
            Line("expected", $"{rows.DisagreeingCount} verdict(s) disagree with the expected one", true);
            foreach (var row in rows.Disagreeing)
                Line("", row, true);
        }

        if (rows.FailedCount > 0)
        {
            Line("failed", $"{rows.FailedCount} row(s) whose model the evaluator calls failed", true);
            foreach (var row in rows.Failed)
                Line("", row, true);
        }
    }

    /// <summary>
    /// The run's alarms. A count the report gives and the rows say too is taken as the larger of
    /// the two: a row said loudly is an alarm whatever the report counts.
    /// </summary>
    private static List<string> Alarms(JsonElement report, JsonElement summary, SmtLiveRowScan rows, int differences)
    {
        var alarms = new List<string>();
        Alarm(alarms, Number(summary, "evaluator_solver_disagreements"), "disagreement(s) between the evaluator and the solver");
        Alarm(alarms, Number(summary, "lies"), "lie(s) found");
        Alarm(alarms, Number(summary, "disputed"), "dispute(s) standing");
        Alarm(alarms, Number(summary, "no_result"), "problem(s) without a result");
        Alarm(alarms, Math.Max(Number(report, "expected", "disagree"), rows.DisagreeingCount), "verdict(s) that disagree with the expected one");
        Alarm(alarms, Math.Max(Number(report, "validation", "classes", "failed"), rows.FailedCount), "settled row(s) whose model the evaluator calls failed");

        if (Text(report, "run", "sampling") == SHORT)
            alarms.Add("the sampling asked for did not happen (short)");

        if (differences > 0)
            alarms.Add($"the report and the results disagree ({differences} count(s))");

        return alarms;
    }

    /// <summary>
    /// Where the report does not count the rows it was written from: their number, and by
    /// status, verdict, agreement with the expected verdict and the evaluator's class.
    /// </summary>
    private static List<string> Differences(JsonElement report, SmtLiveRowScan rows)
    {
        var differences = new List<string>();
        var counted = Number(report, "problem_set", "rows");
        if (counted != rows.Rows)
            differences.Add($"rows: report {counted}, rows {rows.Rows}");

        Differences(differences, "statuses", At(report, "statuses"), rows.Statuses);
        Differences(differences, "verdicts", At(report, "verdicts"), rows.Verdicts);
        Differences(differences, "expected", At(report, "expected"), rows.Expectations);
        Differences(differences, "validation", At(report, "validation", "classes"), rows.Classes);
        return differences;
    }

    private static void Differences(List<string> differences, string name, JsonElement counts, IReadOnlyDictionary<string, int> seen)
    {
        var words = counts.ValueKind == JsonValueKind.Object ? counts.EnumerateObject().Select(member => member.Name) : [];
        foreach (var word in words.Union(seen.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var reported = Number(counts, word);
            var found = seen.GetValueOrDefault(word);
            if (reported != found)
                differences.Add($"{name} {word}: report {reported}, rows {found}");
        }
    }

    private static void Alarm(List<string> alarms, long count, string what)
    {
        if (count > 0)
            alarms.Add($"{count} {what}");
    }

    /// <summary>The indices the integrity document names one by one: their ids are read from the results.</summary>
    private static HashSet<int> Listed(JsonElement integrity)
    {
        return Items(integrity, "disputes").Take(SHOWN_ROWS)
            .Concat(Items(integrity, "evaluator_solver_disagreements"))
            .Select(item => (int)Number(item, "index"))
            .ToHashSet();
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string name)
    {
        var items = At(element, name);
        return items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }

    private static string Indices(JsonElement element, params string[] path)
    {
        var items = At(element, path);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return "-";

        var shown = items.EnumerateArray().Take(SHOWN_INDICES).Select(item => item.GetRawText());
        return string.Join(" ", shown) + (items.GetArrayLength() > SHOWN_INDICES ? $" and {items.GetArrayLength() - SHOWN_INDICES} more" : "");
    }

    private static string Head(string hash)
    {
        return hash.Length > 12 ? hash[..12] + "..." : hash;
    }

    private void Line(string label, string text, bool loud = false)
    {
        var prefix = loud ? LOUD : "";
        m_output.WriteLine(label.Length == 0 ? $"{prefix}{"",-10}  {text}" : $"{prefix}{label,-10}: {text}");
    }

    #endregion
}
