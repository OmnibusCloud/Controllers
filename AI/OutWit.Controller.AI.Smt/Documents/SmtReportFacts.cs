using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>
/// What the report says of a run, counted as the rows of the results are written: the statuses,
/// the verdicts, the unknowns by reason, the expected agreement, the evaluator's classes by logic,
/// the copies and how disputes ended, the long tail, the attribution, and for enumerations the
/// problems with exactly one model and those with more. A record a problem at most (the long tail
/// and the lists of indices), never a model.
/// </summary>
internal sealed class SmtReportFacts
{
    #region Constants

    /// <summary>The most logics the classes are counted by one by one; the rest are counted together.</summary>
    public const int MAX_LOGICS = 64;

    /// <summary>The most pairs of source and licence the attribution names; the problems of the rest are counted together.</summary>
    public const int MAX_ATTRIBUTIONS = 1_000;

    /// <summary>The name the classes of the logics past <see cref="MAX_LOGICS"/> are counted under.</summary>
    public const string OTHER = "(other)";

    #endregion

    #region Functions

    /// <summary>Counts a refused row of the set.</summary>
    public void CountRefused()
    {
        Count(Statuses, SmtRunWords.REFUSED);
        Count(Expectations, SmtRunWords.NOT_APPLICABLE);
    }

    /// <summary>Counts a taken problem by how it was settled.</summary>
    /// <param name="data">The problem as it travelled.</param>
    /// <param name="problem">The problem as the host's sanitizer took it.</param>
    /// <param name="ruling">How it was settled.</param>
    /// <param name="row">Its settled row, or null.</param>
    public void CountTaken(SmtProblemData data, SmtProblem problem, SmtRuling ruling, SmtResultData? row)
    {
        Count(Statuses, row?.Status ?? ruling.HostStatus);
        Count(Expectations, SmtResultsWriter.Agreement(data, row));
        CountAttribution(problem);

        if (data.Query == SmtQuery.Enumerate)
            HasEnumerations = true;

        // Two answers settled as one machine's (a lone answer of the first fan-out and one of the
        // repair round) are not two machines.
        if (ruling.Copies >= SmtRulings.SAMPLE_COPIES && ruling.SettledBy != SmtRunWords.ONE_COPY)
        {
            TwoOrMoreCopies++;
            if (ruling.Agreed)
                AgreedCopies++;
        }

        if (ruling.Dispute != null)
            Count(DisputeOutcomes, ruling.Dispute.Outcome);

        if (ruling.SettledBy.Length > 0)
            Count(SettledBy, ruling.SettledBy);

        if (row == null)
            return;

        if (row.Status is SmtResultWords.OK or SmtResultWords.OUTPUT_EXCEEDED && row.Verdict != SmtResultWords.NONE)
            Count(Verdicts, row.Verdict);

        if (row.Verdict == SmtResultWords.UNKNOWN)
            Count(Unknowns, row.Reason);

        if (row.Verdict == SmtResultWords.UNSAT && ruling.SettledBy == SmtRunWords.ONE_COPY)
            UnsatOneCopy.Add(data.Index);

        if (ruling.Check.Class != SmtRunWords.NOT_CHECKED)
        {
            CountClass(problem.Logic ?? "", ruling.Check.Class);
            if (ruling.Check.Class == SmtRunWords.PARTIAL)
                Count(PartialReasons, ruling.Check.Reason);
        }

        Tail.Add(new SmtTailEntry(data.Index, row.ResourceCount, row.Fuel, StoppedBy(row)));

        if (data.Query == SmtQuery.Enumerate && row.Status == SmtResultWords.OK)
            CountEnumeration(data.Index, row);
    }

    /// <summary>The long tail, sorted: the most fuel first, then the largest resource count, then the index.</summary>
    /// <returns>The entries.</returns>
    public IEnumerable<SmtTailEntry> SortedTail()
    {
        return Tail.OrderByDescending(entry => entry.Fuel).ThenByDescending(entry => entry.ResourceCount).ThenBy(entry => entry.Index);
    }

    private static void Count(SortedDictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static string StoppedBy(SmtResultData row)
    {
        return row.Verdict == SmtResultWords.UNKNOWN && row.Reason is SmtResultWords.RLIMIT or SmtResultWords.FUEL or SmtResultWords.MEMOUT ? row.Reason : "";
    }

    private void CountClass(string logic, string validation)
    {
        if (!Classes.ContainsKey(logic) && Classes.Count >= MAX_LOGICS)
            logic = OTHER;

        if (!Classes.TryGetValue(logic, out var counts))
            Classes[logic] = counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        Count(counts, validation);
        Count(ClassTotals, validation);
    }

    private void CountAttribution(SmtProblem problem)
    {
        if (problem.Source.Length == 0 && problem.License.Length == 0)
            return;

        var key = (problem.Source, problem.License);
        if (Attributions.TryGetValue(key, out var count))
        {
            Attributions[key] = count + 1;
            return;
        }

        if (Attributions.Count >= MAX_ATTRIBUTIONS)
        {
            OtherAttributed++;
            return;
        }

        Attributions[key] = 1;
    }

    private void CountEnumeration(int index, SmtResultData row)
    {
        if (row.Count == 1 && row.Complete)
            ExactlyOne.Add(index);
        else if (row.Count >= 2)
            TwoOrMore.Add(index);
        else if (row.Count == 0 && row.Complete)
            NoModel++;
        else
            Undecided++;
    }

    #endregion

    #region Properties

    /// <summary>The rows by status.</summary>
    public SortedDictionary<string, int> Statuses { get; } = new(StringComparer.Ordinal);

    /// <summary>The answered rows by verdict.</summary>
    public SortedDictionary<string, int> Verdicts { get; } = new(StringComparer.Ordinal);

    /// <summary>The unknowns by reason.</summary>
    public SortedDictionary<string, int> Unknowns { get; } = new(StringComparer.Ordinal);

    /// <summary>The rows by agreement with their expected verdict.</summary>
    public SortedDictionary<string, int> Expectations { get; } = new(StringComparer.Ordinal);

    /// <summary>The rows with a model to check, by the evaluator's class.</summary>
    public SortedDictionary<string, int> ClassTotals { get; } = new(StringComparer.Ordinal);

    /// <summary>The evaluator's classes by logic.</summary>
    public SortedDictionary<string, SortedDictionary<string, int>> Classes { get; } = new(StringComparer.Ordinal);

    /// <summary>The partial classes by reason.</summary>
    public SortedDictionary<string, int> PartialReasons { get; } = new(StringComparer.Ordinal);

    /// <summary>The problems with two or more answers that do not rest on one machine's answer.</summary>
    public int TwoOrMoreCopies { get; private set; }

    /// <summary>Of those, the problems whose answers all gave one digest.</summary>
    public int AgreedCopies { get; private set; }

    /// <summary>The disputes by outcome.</summary>
    public SortedDictionary<string, int> DisputeOutcomes { get; } = new(StringComparer.Ordinal);

    /// <summary>The answers by how they were settled.</summary>
    public SortedDictionary<string, int> SettledBy { get; } = new(StringComparer.Ordinal);

    /// <summary>The problems answered <c>unsat</c> by one machine only (settled by <see cref="SmtRunWords.ONE_COPY"/>).</summary>
    public List<int> UnsatOneCopy { get; } = [];

    /// <summary>Every problem with a settled row, with what it cost.</summary>
    public List<SmtTailEntry> Tail { get; } = [];

    /// <summary>The pairs of source and licence, with their problems.</summary>
    public Dictionary<(string Source, string License), int> Attributions { get; } = new();

    /// <summary>The problems of the pairs past <see cref="MAX_ATTRIBUTIONS"/>.</summary>
    public int OtherAttributed { get; private set; }

    /// <summary>Whether the run has an enumeration.</summary>
    public bool HasEnumerations { get; private set; }

    /// <summary>The enumerations that found exactly one model and showed there is no other.</summary>
    public List<int> ExactlyOne { get; } = [];

    /// <summary>The enumerations that found two models or more.</summary>
    public List<int> TwoOrMore { get; } = [];

    /// <summary>The enumerations that showed there is no model.</summary>
    public int NoModel { get; private set; }

    /// <summary>The enumerations that did not decide: one model and no word on a second, or none and no word.</summary>
    public int Undecided { get; private set; }

    #endregion
}
