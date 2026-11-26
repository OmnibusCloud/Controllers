using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>compare</c>: two runs of the same problem set, each a folder <c>run</c> wrote. Every row's
/// settled answer must be the same, by every field of a row that does not depend on where the
/// work was placed (all but how many machines answered, whether they agreed and how the answer
/// was settled): the status, the verdict, the models, the values, the validation, the digest,
/// the stats. And the provenance must be the same: the profile, the module, the engine, the
/// controller, the options and the set. Every difference is printed; any makes the exit code
/// <see cref="SmtLiveExitCodes.DIFFER"/>.
/// The results are read a row at a time, both files side by side.
/// </summary>
internal sealed class SmtLiveCompare
{
    #region Constants

    /// <summary>The fields of a row that depend on where the work was placed, which a comparison leaves out.</summary>
    public static readonly IReadOnlyList<string> PLACEMENT = ["copies", "agreed", "settled_by"];

    private const int SHOWN = 20;
    private const int DIFFERENCES_PER_ROW = 4;
    private const int PROVENANCE_DIFFERENCES = 20;

    #endregion

    #region Fields

    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    public SmtLiveCompare(TextWriter output)
    {
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Compares two runs.
    /// </summary>
    /// <param name="left">The folder of one run.</param>
    /// <param name="right">The folder of the other.</param>
    /// <returns>The exit code: 0 when they are the same, 4 when they differ, 2 when a document is not there.</returns>
    public int Run(string left, string right)
    {
        foreach (var folder in new[] { left, right })
        {
            foreach (var document in new[] { SmtLiveDocuments.RESULTS, SmtLiveDocuments.PROVENANCE })
            {
                if (File.Exists(Path.Combine(folder, document.File)))
                    continue;

                m_output.WriteLine($"{Path.Combine(folder, document.File)} is not there: compare takes two folders run wrote");
                return SmtLiveExitCodes.USAGE;
            }
        }

        m_output.WriteLine($"compare  : {left} and {right}");

        var (rows, differing) = CompareRows(Path.Combine(left, SmtLiveDocuments.RESULTS.File), Path.Combine(right, SmtLiveDocuments.RESULTS.File));
        var provenance = CompareProvenance(Path.Combine(left, SmtLiveDocuments.PROVENANCE.File), Path.Combine(right, SmtLiveDocuments.PROVENANCE.File));

        if (differing == 0 && provenance)
        {
            m_output.WriteLine("result   : the same settled answers and the same provenance");
            return SmtLiveExitCodes.OK;
        }

        m_output.WriteLine($"{SmtLiveRunSummary.LOUD}result   : THE RUNS DIFFER: {differing} of {rows} row(s){(provenance ? "" : ", and the provenance")}");
        return SmtLiveExitCodes.DIFFER;
    }

    private (int Rows, int Differing) CompareRows(string leftPath, string rightPath)
    {
        var rows = 0;
        var differing = 0;
        var shown = new List<string>();

        using (var left = new StreamReader(leftPath))
        using (var right = new StreamReader(rightPath))
        {
            while (true)
            {
                var a = NextRow(left);
                var b = NextRow(right);
                if (a == null && b == null)
                    break;

                rows++;
                var differences = RowDifferences(a, b, rows);
                if (differences == null)
                    continue;

                differing++;
                if (shown.Count < SHOWN)
                    shown.Add(differences);
            }
        }

        m_output.WriteLine($"rows     : {rows} compared by every field but {string.Join(", ", PLACEMENT)}: {(differing == 0 ? "all the same" : $"{differing} DIFFER")}");
        foreach (var line in shown)
            m_output.WriteLine($"  {line}");

        if (differing > shown.Count)
            m_output.WriteLine($"  and {differing - shown.Count} more");

        return (rows, differing);
    }

    /// <summary>What differs between the rows at one place of the two files; null when nothing does.</summary>
    private static string? RowDifferences(string? left, string? right, int place)
    {
        if (left == null || right == null)
            return $"row {place}: only in the {(left == null ? "second" : "first")} run";

        try
        {
            using var a = JsonDocument.Parse(left);
            using var b = JsonDocument.Parse(right);

            // A row of the results is one object; JSON of another kind is a difference, not a crash.
            if (a.RootElement.ValueKind != JsonValueKind.Object || b.RootElement.ValueKind != JsonValueKind.Object)
                return $"row {place}: not a JSON object in one of the runs";

            var differences = new List<string>();
            var names = a.RootElement.EnumerateObject().Select(member => member.Name)
                .Union(b.RootElement.EnumerateObject().Select(member => member.Name), StringComparer.Ordinal)
                .Where(name => !PLACEMENT.Contains(name));
            foreach (var name in names)
                SmtLiveJson.Differences(SmtLiveJson.At(a.RootElement, name), SmtLiveJson.At(b.RootElement, name), name, differences, DIFFERENCES_PER_ROW);

            if (differences.Count == 0)
                return null;

            return $"[{SmtLiveJson.Text(a.RootElement, "index")}] {SmtLiveJson.Text(a.RootElement, "id")}: {string.Join("; ", differences)}";
        }
        catch (JsonException)
        {
            return $"row {place}: not JSON in one of the runs";
        }
    }

    private bool CompareProvenance(string leftPath, string rightPath)
    {
        var differences = new List<string>();
        try
        {
            using var a = JsonDocument.Parse(File.ReadAllBytes(leftPath));
            using var b = JsonDocument.Parse(File.ReadAllBytes(rightPath));
            SmtLiveJson.Differences(a.RootElement, b.RootElement, "$", differences, PROVENANCE_DIFFERENCES);
        }
        catch (JsonException)
        {
            differences.Add("a provenance is not JSON");
        }

        m_output.WriteLine(differences.Count == 0 ? "provenance: the same" : "provenance: DIFFERS");
        foreach (var difference in differences)
            m_output.WriteLine($"  {difference}");

        return differences.Count == 0;
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
}
