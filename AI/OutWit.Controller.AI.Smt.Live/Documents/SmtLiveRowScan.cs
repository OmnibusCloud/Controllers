using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// One pass over the results of a run, a row at a time: the ids of the problems the integrity
/// document names by index, and the rows a lab must look at, the first few of each kind in full
/// and all of them counted: the rows not answered <c>ok</c>, the verdicts that disagree with the
/// expected one, and the models the evaluator calls failed.
/// </summary>
internal sealed class SmtLiveRowScan
{
    #region Constants

    private const int MESSAGE_CHARACTERS = 100;

    #endregion

    #region Fields

    private readonly int m_shown;

    #endregion

    #region Constructors

    private SmtLiveRowScan(int shown)
    {
        m_shown = shown;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the results.
    /// </summary>
    /// <param name="path">The results file (JSON lines).</param>
    /// <param name="wanted">The indices whose ids are wanted.</param>
    /// <param name="shown">The most rows of each kind kept in full.</param>
    /// <returns>What the pass found.</returns>
    public static SmtLiveRowScan Scan(string path, IReadOnlySet<int> wanted, int shown)
    {
        var scan = new SmtLiveRowScan(shown);
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                scan.Take(document.RootElement, wanted);
            }
            catch (JsonException)
            {
                // The document check says so; this pass only reads what it can.
            }
        }

        return scan;
    }

    private void Take(JsonElement row, IReadOnlySet<int> wanted)
    {
        Rows++;
        var index = (int)SmtLiveJson.Number(row, "index");
        var id = SmtLiveJson.Text(row, "id");
        if (wanted.Contains(index))
            Ids[index] = id;

        var status = SmtLiveJson.Text(row, "status");
        if (status != SmtResultWords.OK)
        {
            NotOkCount++;
            NotOkByStatus[status] = NotOkByStatus.GetValueOrDefault(status) + 1;
            var line = SmtLiveJson.Number(row, "line") > 0 ? $", line {SmtLiveJson.Number(row, "line")}" : "";
            Keep(NotOk, $"[{index}] {id}: {status} {SmtLiveJson.Text(row, "code")}{line}: {Cut(SmtLiveJson.Text(row, "message"))}");
        }

        if (SmtLiveJson.Text(row, "expected_agreement") == SmtRunWords.DISAGREE)
        {
            DisagreeingCount++;
            Keep(Disagreeing, $"[{index}] {id}: verdict {SmtLiveJson.Text(row, "verdict")}, expected {SmtLiveJson.Text(row, "expected")}");
        }

        if (SmtLiveJson.Text(row, "validation") == SmtRunWords.FAILED)
        {
            FailedCount++;
            Keep(Failed, $"[{index}] {id}: {SmtLiveJson.Text(row, "validation_reason")} {SmtLiveJson.Text(row, "validation_detail")} (settled by {SmtLiveJson.Text(row, "settled_by")})");
        }
    }

    private void Keep(List<string> rows, string row)
    {
        if (rows.Count < m_shown)
            rows.Add(row);
    }

    private static string Cut(string text)
    {
        return text.Length <= MESSAGE_CHARACTERS ? text : text[..MESSAGE_CHARACTERS] + "...";
    }

    #endregion

    #region Properties

    /// <summary>The rows read.</summary>
    public int Rows { get; private set; }

    /// <summary>The ids of the wanted indices, by index.</summary>
    public Dictionary<int, string> Ids { get; } = [];

    /// <summary>The rows whose status is not <c>ok</c>.</summary>
    public int NotOkCount { get; private set; }

    /// <summary>The same by status.</summary>
    public SortedDictionary<string, int> NotOkByStatus { get; } = new(StringComparer.Ordinal);

    /// <summary>The first of them in full.</summary>
    public List<string> NotOk { get; } = [];

    /// <summary>The rows whose verdict disagrees with the expected one.</summary>
    public int DisagreeingCount { get; private set; }

    /// <summary>The first of them in full.</summary>
    public List<string> Disagreeing { get; } = [];

    /// <summary>The rows whose model the evaluator calls failed.</summary>
    public int FailedCount { get; private set; }

    /// <summary>The first of them in full.</summary>
    public List<string> Failed { get; } = [];

    #endregion
}
