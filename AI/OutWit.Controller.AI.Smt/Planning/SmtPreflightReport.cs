using OutWit.Controller.AI.Smt.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;

namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// Makes the preflight of a problem set and a run's options (<see cref="SmtPreflightData"/>): the
/// set read the way a run reads it, the options checked by name, the budget as numbers, the
/// upper bound (problems times the hard cap: no duration is estimated, since how long an SMT
/// problem takes cannot be told from its text), the memory of one session on a node, and the
/// packages a run would cut the set into.
/// </summary>
internal static class SmtPreflightReport
{
    #region Constants

    /// <summary>The message of a set without a row.</summary>
    public const string EMPTY = "The problem set has no row: a run would be refused.";

    /// <summary>The message of a set whose every row is refused.</summary>
    public const string ALL_REFUSED = "Every row of the problem set is refused: a run would send nothing to a node, and its results would say why for each row.";

    /// <summary>The message of the upper bound.</summary>
    public const string BOUND = "The upper bound is the taken problems times the hard cap, in seconds of the reference machine: a bound, not an estimate. How long an SMT problem takes cannot be told from its text.";

    #endregion

    #region Functions

    /// <summary>Reads a set and makes its preflight.</summary>
    /// <param name="path">The set's local file.</param>
    /// <param name="options">The run's options as given; null for the defaults.</param>
    /// <returns>The preflight.</returns>
    public static SmtPreflightData Of(string path, SmtOptionsData? options)
    {
        options ??= new SmtOptionsData();

        var problems = SmtOptionsRules.Check(options).ToList();
        if (!SmtProfile.Is(options.Profile))
            problems.Add($"'profile' names a profile this controller does not have; it has '{SmtProfile.ID}'");

        var readable = SmtOptionsRules.Readable(options);
        var scan = SmtProblemSetScan.Read(path, readable, null);
        return Of(scan, readable, problems);
    }

    /// <summary>Makes the preflight of a read set.</summary>
    /// <param name="scan">The set, read.</param>
    /// <param name="options">The options it was read with, in range.</param>
    /// <param name="problems">What is wrong with the options as given, one sentence each.</param>
    /// <returns>The preflight.</returns>
    public static SmtPreflightData Of(SmtProblemSetScan scan, SmtOptionsData options, IReadOnlyList<string> problems)
    {
        var summary = scan.Summary;
        var resolved = SmtOptionsRules.Resolve(options);
        var envelope = SmtProfile.Envelope(options);

        var report = new SmtPreflightData
        {
            Runnable = problems.Count == 0 && summary.Rows > 0,
            Rows = summary.Rows,
            Accepted = summary.Accepted,
            Refused = summary.Refused,
            BytesRead = summary.BytesRead,
            Sha256 = scan.Sha256,
            RefusalCodes = Counts(summary.RefusalCodes),
            Refusals = [.. summary.Refusals.Take(SmtPreflightData.MAX_REFUSALS).Select(refusal => refusal.Clone())],
            Logics = Counts(summary.Logics),
            OtherLogics = summary.OtherLogics,
            Queries = Counts(summary.Queries.ToDictionary(pair => SmtResultsWriter.Query(pair.Key), pair => pair.Value)),
            Expected = Counts(summary.Expected.ToDictionary(pair => SmtResultsWriter.Expected(pair.Key), pair => pair.Value)),
            Stripped = Counts(summary.Stripped),
            Licenses = Counts(summary.Licenses),
            OtherLicenses = summary.OtherLicenses,
            OptionProblems = [.. problems],
            Profile = SmtProfile.ID,
            ResourceLimit = envelope.ResourceLimit,
            HardCapSeconds = resolved.HardCapSeconds,
            Fuel = envelope.Fuel,
            MemoryMegabytes = envelope.MemoryMegabytes,
            MaxModels = envelope.MaxModels,
            Seed = envelope.Seed,
            SamplingRate = resolved.SamplingRate,
            SessionMemoryBytes = SmtSandboxDegree.SessionBytes(resolved),
            UpperBoundSeconds = (long)summary.Accepted * resolved.HardCapSeconds,
            Packages = SmtPackagePlanner.Count(scan.Characters),
            ProblemsPerPackage = SmtPackagePlanner.ProblemsPerPackage(summary.Accepted)
        };

        if (summary.Rows == 0)
            report.Messages.Add(EMPTY);
        else if (summary.Accepted == 0)
            report.Messages.Add(ALL_REFUSED);

        report.Messages.Add(BOUND);
        return report;
    }

    #endregion

    #region Tools

    private static List<SmtCountData> Counts(IEnumerable<KeyValuePair<string, int>> counts)
    {
        return [.. counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => SmtCountData.Of(pair.Key, pair.Value))];
    }

    #endregion
}
