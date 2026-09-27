using System.Globalization;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Turns a finished case's <c>postProcessing/</c> tree into the response
/// row: for every requested response, the last row of every data file in
/// its latest time directory, each column a value named
/// <c>&lt;response&gt;.&lt;column&gt;</c> (with the file's name in between when
/// a function object writes more than one file). The time column is left out;
/// it is the same for every column and the result carries it once. Given the
/// run's final time, a file whose last row is earlier - a function object
/// that stopped writing before the run ended - is not reported: a stale value
/// is worse than none, and a note names it.
/// </summary>
public static class FoamResponseExtractor
{
    #region Constants

    private const string POST_PROCESSING = "postProcessing";

    private const string TIME_COLUMN = "Time";

    private const string NOTES_FILE = "log.responses";

    /// <summary>Relative tolerance of "the same time": a transient time loop carries its last digit's noise.</summary>
    private const double TIME_TOLERANCE = 1e-9;

    #endregion

    #region Functions

    /// <summary>
    /// Extracts the requested responses.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="request">The request; null yields an empty row.</param>
    /// <returns>The row; a response whose files are missing contributes nothing.</returns>
    public static FoamResponseRowData Extract(string caseDirectory, FoamExtractionRequestData? request)
    {
        return Extract(caseDirectory, request, null, null);
    }

    /// <summary>
    /// Extracts the requested responses whose last row is at the run's final time.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="request">The request; null yields an empty row.</param>
    /// <param name="finalTime">The time the run ended at; null skips the check.</param>
    /// <param name="notes">Receives one sentence per file left out; null discards them.</param>
    /// <returns>The row; a response whose files are missing or stale contributes nothing.</returns>
    public static FoamResponseRowData Extract(string caseDirectory, FoamExtractionRequestData? request, double? finalTime, ICollection<string>? notes)
    {
        var row = new FoamResponseRowData();
        if (request == null)
            return row;

        foreach (var response in request.Responses)
        {
            var directory = LatestTimeDirectory(Path.Combine(caseDirectory, POST_PROCESSING, response.Name));
            if (directory == null)
                continue;

            var files = Directory.EnumerateFiles(directory)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();

            foreach (var file in files)
            {
                var table = FoamPostProcessingReader.ReadLastRow(file);
                if (table == null)
                    continue;

                if (finalTime is { } final && IsStale(table.Value.Columns, table.Value.Values, final, out var time))
                {
                    notes?.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{response.Name}: the last row of {Path.GetFileName(file)} is at time {time}, the run ended at {final} - not reported."));
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(file);
                var prefix = files.Count > 1 ? $"{response.Name}.{stem}" : response.Name;

                for (var index = 0; index < table.Value.Columns.Count; index++)
                {
                    var column = table.Value.Columns[index];
                    if (index == 0 && string.Equals(column, TIME_COLUMN, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = table.Value.Values[index];
                    if (double.IsNaN(value))
                        continue;

                    row.Values.Add(new FoamResponseValueData { Name = $"{prefix}.{column}", Value = value });
                }
            }
        }

        return row;
    }

    /// <summary>
    /// Writes the notes of an extraction beside the step logs (<c>log.responses</c>),
    /// where they travel with the logs; no notes, no file.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="notes">The notes of <see cref="Extract(string, FoamExtractionRequestData?, double?, ICollection{string}?)"/>.</param>
    public static void WriteNotes(string caseDirectory, IReadOnlyCollection<string> notes)
    {
        if (notes.Count == 0)
            return;

        File.WriteAllLines(Path.Combine(caseDirectory, NOTES_FILE), notes);
    }

    /// <summary>
    /// The time directory with the largest numeric name under a function
    /// object's output directory.
    /// </summary>
    /// <param name="functionDirectory">The <c>postProcessing/&lt;name&gt;</c> directory.</param>
    /// <returns>The latest time directory, or null when there is none.</returns>
    public static string? LatestTimeDirectory(string functionDirectory)
    {
        if (!Directory.Exists(functionDirectory))
            return null;

        string? latest = null;
        var latestTime = double.NegativeInfinity;

        foreach (var directory in Directory.EnumerateDirectories(functionDirectory))
        {
            var name = Path.GetFileName(directory);
            if (!double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out var time))
                continue;

            if (time > latestTime)
            {
                latestTime = time;
                latest = directory;
            }
        }

        return latest;
    }

    #endregion

    #region Tools

    // A last row whose time column is not the final time.
    private static bool IsStale(IReadOnlyList<string> columns, IReadOnlyList<double> values, double finalTime, out double time)
    {
        time = double.NaN;
        if (columns.Count == 0 || values.Count == 0 || !string.Equals(columns[0], TIME_COLUMN, StringComparison.OrdinalIgnoreCase))
            return false;

        time = values[0];
        return System.Math.Abs(time - finalTime) > TIME_TOLERANCE * System.Math.Max(1.0, System.Math.Abs(finalTime));
    }

    #endregion
}
