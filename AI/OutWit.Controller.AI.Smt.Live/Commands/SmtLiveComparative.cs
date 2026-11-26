using System.Globalization;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Comparative;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Model.Problems;
using static OutWit.Controller.AI.Smt.Live.Documents.SmtLiveJson;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>comparative</c>: the comparative table of one set, from a folder <c>local</c> wrote and a
/// folder <c>run</c> wrote for the same set. First whether the two ran the same thing: the same
/// set, the same module, the same options but the sampling rate. Then each side's measured wall
/// time from the tool's start to the documents on disk and its parts, its problems a second, its
/// verdicts, its failed validations, its retries and the disagreements among its copies. Then the
/// rows side by side, digest against digest (<see cref="SmtLiveRowComparison"/>), with the flips.
/// Then the crossover (<see cref="SmtLiveCrossover"/>), with what it assumes, when a fleet run of
/// a small set measured the fleet's fixed cost. Every number is read from what the two commands
/// wrote; nothing is estimated and shown as measured.
/// </summary>
internal sealed class SmtLiveComparative
{
    #region Constants

    private const int LABEL = 34;
    private const int COLUMN = 24;
    private const string NOT_MEASURED = "not measured";

    // The most differences of the two runs' options a comparison names.
    private const int MAX_DIFFERENCES_SHOWN = 10;

    #endregion

    #region Fields

    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    public SmtLiveComparative(TextWriter output)
    {
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Prints the comparative table of a local run and a fleet run of one set.
    /// </summary>
    /// <param name="localFolder">The folder <c>local</c> wrote.</param>
    /// <param name="fleetFolder">The folder <c>run</c> wrote.</param>
    /// <param name="fixedFolder">The folder <c>run</c> wrote for a small set, which measures the fleet's fixed cost; null without one.</param>
    /// <returns>0 when both ran the same set, module and options and every row is the same; 1 when anything differs; 2 when a document is not there.</returns>
    public int Run(string localFolder, string fleetFolder, string? fixedFolder)
    {
        var missing = Missing(localFolder, true) ?? Missing(fleetFolder, false) ?? (fixedFolder != null && !SmtLiveMeasured.IsIn(fixedFolder, false)
            ? $"{Path.Combine(fixedFolder, SmtLiveFleetFacts.FILE)} is not there: --fixed takes a folder run wrote for a small set"
            : null);
        if (missing != null)
        {
            m_output.WriteLine(missing);
            return SmtLiveExitCodes.USAGE;
        }

        SmtLiveMeasured local;
        SmtLiveMeasured? fleet;
        SmtLiveMeasured? small;
        try
        {
            local = SmtLiveMeasured.ReadLocal(localFolder);
            fleet = SmtLiveMeasured.IsIn(fleetFolder, false) ? SmtLiveMeasured.ReadFleet(fleetFolder) : null;
            small = fixedFolder == null ? null : SmtLiveMeasured.ReadFleet(fixedFolder);
        }
        catch (InvalidDataException exception)
        {
            m_output.WriteLine($"a measurement cannot be read: {exception.Message}");
            return SmtLiveExitCodes.USAGE;
        }

        m_output.WriteLine($"comparative: {localFolder} (local) and {fleetFolder} (fleet)");

        using var localDocs = SmtLiveRunDocuments.Read(localFolder);
        using var fleetDocs = SmtLiveRunDocuments.Read(fleetFolder);
        var same = PrintSameness(localDocs, fleetDocs);

        PrintTable(local, fleet, localDocs, fleetDocs);

        var rows = SmtLiveRowComparison.Of(Path.Combine(localFolder, SmtLiveDocuments.RESULTS.File), Path.Combine(fleetFolder, SmtLiveDocuments.RESULTS.File));
        PrintRows(rows);

        PrintCrossover(local, fleet, small);
        m_output.WriteLine("operator  : not measured by the tool; SCRIPT.md of the comparative test lists the steps of each side");

        if (same && rows.AllSame)
        {
            m_output.WriteLine("result    : the same set, module and options, and the same answer at the same cost on every row");
            return SmtLiveExitCodes.OK;
        }

        m_output.WriteLine($"{SmtLiveRunSummary.LOUD}result    : THE TWO RUNS DIFFER{(same ? "" : " in what they ran")}{(rows.AllSame ? "" : ", row by row")}");
        return SmtLiveExitCodes.FAILED;
    }

    private static string? Missing(string folder, bool local)
    {
        foreach (var document in SmtLiveDocuments.RUN)
        {
            if (!File.Exists(Path.Combine(folder, document.File)))
                return $"{Path.Combine(folder, document.File)} is not there: comparative takes a folder local wrote and a folder run wrote";
        }

        return local && !SmtLiveMeasured.IsIn(folder, true) ? $"{Path.Combine(folder, SmtLiveLocalFacts.FILE)} is not there: the first folder is one local wrote" : null;
    }

    private bool PrintSameness(SmtLiveRunDocuments local, SmtLiveRunDocuments fleet)
    {
        var setA = Text(local.Provenance, "problem_set", "sha256");
        var setB = Text(fleet.Provenance, "problem_set", "sha256");
        var sameSet = setA.Length > 0 && setA == setB;
        m_output.WriteLine(sameSet
            ? $"set       : the same: sha256 {setA}, {Text(local.Provenance, "problem_set", "rows")} row(s)"
            : $"{SmtLiveRunSummary.LOUD}set       : NOT THE SAME: local sha256 {setA}, fleet sha256 {setB}");

        var differences = new List<string>();
        Differences(Without(At(local.Provenance, "options"), "sampling_rate"), Without(At(fleet.Provenance, "options"), "sampling_rate"), "options", differences, MAX_DIFFERENCES_SHOWN);
        m_output.WriteLine(differences.Count == 0
            ? $"options   : the same but the sampling rate (local {Text(local.Provenance, "sampling_rate")}, fleet {Text(fleet.Provenance, "sampling_rate")})"
            : $"{SmtLiveRunSummary.LOUD}options   : DIFFER: {string.Join("; ", differences)}");

        var moduleA = Text(local.Provenance, "profile", "module_sha256");
        var moduleB = Text(fleet.Provenance, "profile", "module_sha256");
        var sameModule = moduleA.Length > 0 && moduleA == moduleB;
        m_output.WriteLine(sameModule
            ? $"module    : the same: sha256 {moduleA} ({Text(local.Provenance, "profile", "id")})"
            : $"{SmtLiveRunSummary.LOUD}module    : NOT THE SAME: local sha256 {moduleA}, fleet sha256 {moduleB}");

        m_output.WriteLine($"controller: local {Text(local.Provenance, "controller")}, fleet {Text(fleet.Provenance, "controller")}");
        return sameSet && sameModule && differences.Count == 0;
    }

    private void PrintTable(SmtLiveMeasured local, SmtLiveMeasured? fleet, SmtLiveRunDocuments localDocs, SmtLiveRunDocuments fleetDocs)
    {
        m_output.WriteLine($"local     : {local.Description}");
        m_output.WriteLine($"fleet     : {fleet?.Description ?? "the fleet's machines (not named)"}");
        m_output.WriteLine();
        Row("", "local", "fleet");
        Row("wall time, end to end", Clock(local.Wall), fleet == null ? $"{NOT_MEASURED} (no {SmtLiveFleetFacts.FILE})" : Clock(fleet.Wall));

        var parts = Math.Max(local.Parts.Count, fleet?.Parts.Count ?? 0);
        for (var index = 0; index < parts; index++)
        {
            var a = index < local.Parts.Count ? $"{local.Parts[index].Name} {Clock(local.Parts[index].Seconds)}" : "";
            var b = fleet != null && index < fleet.Parts.Count ? $"{fleet.Parts[index].Name} {Clock(fleet.Parts[index].Seconds)}" : "";
            Row(index == 0 ? "  of it" : "", a, b);
        }

        Row("problems taken", Text(localDocs.Report, "problem_set", "accepted"), Text(fleetDocs.Report, "problem_set", "accepted"));
        Row("problems a second, end to end", Rate(local), fleet == null ? NOT_MEASURED : Rate(fleet));
        Row("statuses", NonZero(localDocs.Report, "statuses"), NonZero(fleetDocs.Report, "statuses"));
        Row("verdicts sat / unsat / unknown", Verdicts(localDocs.Report), Verdicts(fleetDocs.Report));
        Row("expected agree / disagree", Pair(localDocs.Report, "expected", "agree", "disagree"), Pair(fleetDocs.Report, "expected", "agree", "disagree"));
        Row("validated / partial / failed", Classes(localDocs.Report), Classes(fleetDocs.Report));
        Row("failed validations", Text(localDocs.Report, "validation", "classes", "failed"), Text(fleetDocs.Report, "validation", "classes", "failed"));
        Row("sampling", Sampling(localDocs.Report), Sampling(fleetDocs.Report));
        Row("answers taken (copies)", Text(localDocs.Integrity, "summary", "copies_made"), Text(fleetDocs.Integrity, "summary", "copies_made"));
        Row("copies that disagreed", Text(localDocs.Integrity, "summary", "disagreements"), Text(fleetDocs.Integrity, "summary", "disagreements"));
        Row("retries: repairs / disputes", Pair(localDocs.Integrity, "summary", "repairs", "disputes"), Pair(fleetDocs.Integrity, "summary", "repairs", "disputes"));
        Row("node faults / refused results", Pair(localDocs.Integrity, "summary", "node_faults", "rejected_results"), Pair(fleetDocs.Integrity, "summary", "node_faults", "rejected_results"));
        Row("unanswered packages / no result", Pair(localDocs.Integrity, "summary", "unanswered_packages", "no_result"),
            Pair(fleetDocs.Integrity, "summary", "unanswered_packages", "no_result"));
        Row("lies / evaluator disagreements", Pair(localDocs.Integrity, "summary", "lies", "evaluator_solver_disagreements"),
            Pair(fleetDocs.Integrity, "summary", "lies", "evaluator_solver_disagreements"));
        m_output.WriteLine();
    }

    private void PrintRows(SmtLiveRowComparison rows)
    {
        var shape = rows.Misplaced + rows.OnlyInOne + rows.Unreadable > 0
            ? $"; {SmtLiveRunSummary.LOUD}{rows.Misplaced} misplaced, {rows.OnlyInOne} on one side only, {rows.Unreadable} unreadable"
            : "";
        m_output.WriteLine($"rows      : {rows.Rows} compared row by row; digests: {rows.SameDigest} of {rows.WithDigests} equal" +
                           $"{(rows.DifferentDigest > 0 ? $", {SmtLiveRunSummary.LOUD}{rows.DifferentDigest} DIFFER" : "")}; without a digest: {rows.SameWithoutDigest} the same" +
                           $"{(rows.DifferentWithoutDigest > 0 ? $", {rows.DifferentWithoutDigest} DIFFER" : "")}{shape}");
        m_output.WriteLine($"verdicts  : {rows.SameVerdict} of {rows.Rows} the same; flips (sat, unsat and unknown against each other): {rows.Flips}");
        foreach (var line in rows.Shown)
            m_output.WriteLine($"  {line}");

        var shown = rows.DifferentDigest + rows.DifferentWithoutDigest + rows.Misplaced + rows.OnlyInOne + rows.Unreadable;
        if (shown > rows.Shown.Count)
            m_output.WriteLine($"  and {shown - rows.Shown.Count} more");
    }

    private void PrintCrossover(SmtLiveMeasured local, SmtLiveMeasured? fleet, SmtLiveMeasured? small)
    {
        if (fleet == null)
        {
            m_output.WriteLine($"crossover : not computed: the fleet run has no {SmtLiveFleetFacts.FILE} (a run of an older smt-live measured nothing)");
            return;
        }

        var first = local.Wall < fleet.Wall ? "this machine" : local.Wall > fleet.Wall ? "the fleet" : "neither";
        m_output.WriteLine($"measured  : at {fleet.Taken} problem(s) {first} finished first ({Clock(local.Wall)} here, {Clock(fleet.Wall)} on the fleet)");

        var crossover = SmtLiveCrossover.Compute(local.Taken, local.Wall, local.Startup ?? 0, fleet.Taken, fleet.Wall,
            small == null ? null : (small.Taken, small.Wall));
        var lines = $"local {Seconds(crossover.LocalFixed)} fixed + {Seconds(crossover.LocalPerProblem)} a problem";
        if (crossover.FleetFixed is { } fleetFixed && crossover.FleetPerProblem is { } fleetCost)
            lines += $"; fleet {Seconds(fleetFixed)} fixed + {Seconds(fleetCost)} a problem";

        var verdict = crossover.Kind switch
        {
            SmtLiveCrossoverKind.FleetAbove => $"the fleet finishes first above {Problems(crossover.Problems)} problems of this kind, this machine below",
            SmtLiveCrossoverKind.FleetBelow => $"the fleet finishes first below {Problems(crossover.Problems)} problems of this kind, this machine above",
            SmtLiveCrossoverKind.FleetAlways => "the fleet finishes first at every size",
            SmtLiveCrossoverKind.LocalAlways when crossover.Problems is { } far =>
                $"this machine finishes first at every size a set may have (the lines cross at {Problems(far)} problems, above the {SmtProblemSetLimits.MAX_ROWS:N0} rows a set may have)",
            SmtLiveCrossoverKind.LocalAlways => "this machine finishes first at every size",
            _ => $"not computed: {crossover.Reason}"
        };

        m_output.WriteLine($"crossover : {verdict}");
        m_output.WriteLine($"  lines   : {lines}");
        if (crossover.Kind == SmtLiveCrossoverKind.NotComputed)
            return;

        m_output.WriteLine($"  assumes : each side's time is a fixed cost and a cost a problem, the same for every problem of a set like this one, under these options");
        m_output.WriteLine($"            local fixed cost = its start-up to the sandbox ready ({Seconds(local.Startup ?? 0)}, the module's compile); the rest grows with the problems");
        m_output.WriteLine($"            fleet = the straight line through two fleet runs measured end to end: the small set's ({small!.Taken} problem(s), {Seconds(small.Wall)}) " +
                           $"and this one's ({fleet.Taken}, {Seconds(fleet.Wall)}), at that fleet's size and this run's sampling rate");
    }

    private void Row(string label, string local, string fleet)
    {
        m_output.WriteLine($"{label,-LABEL}{local,-COLUMN} {fleet}".TrimEnd());
    }

    private static JsonElement Without(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element;

        var copy = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject().Where(member => member.Name != name))
            copy[member.Name] = member.Value;

        return JsonSerializer.SerializeToElement(copy);
    }

    private static string Verdicts(JsonElement report)
    {
        return $"{Text(report, "verdicts", "sat")} / {Text(report, "verdicts", "unsat")} / {Text(report, "verdicts", "unknown")}";
    }

    private static string Classes(JsonElement report)
    {
        return $"{Text(report, "validation", "classes", "validated")} / {Text(report, "validation", "classes", "partial")} / {Text(report, "validation", "classes", "failed")}";
    }

    private static string Sampling(JsonElement report)
    {
        return $"{Text(report, "run", "sampling")} (rate {Text(report, "run", "sampling_rate")})";
    }

    private static string Pair(JsonElement document, string parent, string first, string second)
    {
        return $"{Text(document, parent, first)} / {Text(document, parent, second)}";
    }

    private static string Rate(SmtLiveMeasured measured)
    {
        return measured.Rate.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Problems(double? problems)
    {
        return problems is { } value ? Math.Ceiling(value).ToString("N0", CultureInfo.InvariantCulture) : "-";
    }

    private static string Seconds(double seconds)
    {
        return seconds.ToString(seconds < 10 ? "0.###" : "0.#", CultureInfo.InvariantCulture) + " s";
    }

    private static string Clock(double seconds)
    {
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture);
    }

    #endregion
}
