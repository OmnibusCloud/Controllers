using System.Text.Json;
using static OutWit.Controller.AI.Smt.Live.Documents.SmtLiveJson;

namespace OutWit.Controller.AI.Smt.Live.Comparative;

/// <summary>
/// The results of a local run and a fleet run of one set, row by row (both files are in the set's
/// order, one row a row of the set, and are read side by side, a row at a time). A row's digest
/// (<c>smt-result/1</c>) covers every value of its answer but where and when it was made: equal
/// digests are the strongest sign that the two sides ran the same problem with the same module
/// under the same options and got the same answer at the same cost. A row without a digest (a
/// refused row, or one without an answer) is compared by its status and code. A flip is a row
/// whose verdict differs between the sides.
/// </summary>
internal sealed class SmtLiveRowComparison
{
    #region Constants

    /// <summary>The differing rows kept for printing.</summary>
    public const int SHOWN = 20;

    #endregion

    #region Fields

    private readonly List<string> m_shown = [];

    #endregion

    #region Functions

    /// <summary>
    /// Compares two results files.
    /// </summary>
    /// <param name="localPath">The local run's results.</param>
    /// <param name="fleetPath">The fleet run's results.</param>
    /// <returns>The comparison.</returns>
    public static SmtLiveRowComparison Of(string localPath, string fleetPath)
    {
        var comparison = new SmtLiveRowComparison();
        using var local = new StreamReader(localPath);
        using var fleet = new StreamReader(fleetPath);

        while (true)
        {
            var a = NextRow(local);
            var b = NextRow(fleet);
            if (a == null && b == null)
                break;

            comparison.Rows++;
            comparison.Add(a, b);
        }

        return comparison;
    }

    private void Add(string? local, string? fleet)
    {
        if (local == null || fleet == null)
        {
            OnlyInOne++;
            Show($"row {Rows}: only in the {(local == null ? "fleet" : "local")} run");
            return;
        }

        try
        {
            using var a = JsonDocument.Parse(local);
            using var b = JsonDocument.Parse(fleet);
            Compare(a.RootElement, b.RootElement);
        }
        catch (JsonException)
        {
            Unreadable++;
            Show($"row {Rows}: not JSON in one of the runs");
        }
    }

    private void Compare(JsonElement local, JsonElement fleet)
    {
        var name = $"[{Text(local, "index")}] {Text(local, "id")}";
        if (Text(local, "index") != Text(fleet, "index") || Text(local, "id") != Text(fleet, "id"))
        {
            Misplaced++;
            Show($"{name}: the fleet's row at this place is [{Text(fleet, "index")}] {Text(fleet, "id")}");
            return;
        }

        var verdictA = Text(local, "verdict");
        var verdictB = Text(fleet, "verdict");
        if (verdictA == verdictB)
            SameVerdict++;
        else if (verdictA != "none" && verdictB != "none")
            Flips++;

        var digestA = Text(local, "digest");
        var digestB = Text(fleet, "digest");
        if (digestA.Length > 0 && digestB.Length > 0)
        {
            WithDigests++;
            if (digestA == digestB)
            {
                SameDigest++;
                return;
            }

            DifferentDigest++;
            Show($"{name}: digests differ: local {verdictA} {Short(digestA)} fuel {Text(local, "stats", "fuel")}, " +
                 $"fleet {verdictB} {Short(digestB)} fuel {Text(fleet, "stats", "fuel")}");
            return;
        }

        var answerA = $"{Text(local, "status")} {Text(local, "code")}".Trim();
        var answerB = $"{Text(fleet, "status")} {Text(fleet, "code")}".Trim();
        if (digestA.Length == 0 && digestB.Length == 0 && answerA == answerB)
        {
            SameWithoutDigest++;
            return;
        }

        DifferentWithoutDigest++;
        Show($"{name}: local {answerA}{(digestA.Length > 0 ? " with a digest" : "")}, fleet {answerB}{(digestB.Length > 0 ? " with a digest" : "")}");
    }

    private void Show(string line)
    {
        if (m_shown.Count < SHOWN)
            m_shown.Add(line);
    }

    private static string Short(string digest)
    {
        return digest.Length > 16 ? digest[..16] : digest;
    }

    private static string? NextRow(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
                return line;
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The places compared: the longer file's rows.</summary>
    public int Rows { get; private set; }

    /// <summary>Rows with a digest on both sides.</summary>
    public int WithDigests { get; private set; }

    /// <summary>Of those, the rows whose digests are equal.</summary>
    public int SameDigest { get; private set; }

    /// <summary>Of those, the rows whose digests differ.</summary>
    public int DifferentDigest { get; private set; }

    /// <summary>Rows without a digest on either side, with the same status and code.</summary>
    public int SameWithoutDigest { get; private set; }

    /// <summary>Rows without a digest on one side or both whose answers differ.</summary>
    public int DifferentWithoutDigest { get; private set; }

    /// <summary>Rows whose verdicts are equal.</summary>
    public int SameVerdict { get; private set; }

    /// <summary>Rows that have a verdict on both sides, and different ones.</summary>
    public int Flips { get; private set; }

    /// <summary>Places where the two rows are not the same row of the set.</summary>
    public int Misplaced { get; private set; }

    /// <summary>Places with a row on one side only.</summary>
    public int OnlyInOne { get; private set; }

    /// <summary>Places where a row is not JSON.</summary>
    public int Unreadable { get; private set; }

    /// <summary>The first differences, in the set's order.</summary>
    public IReadOnlyList<string> Shown => m_shown;

    /// <summary>Whether every row is the same on both sides.</summary>
    public bool AllSame => Rows > 0 && SameDigest + SameWithoutDigest == Rows;

    #endregion
}
