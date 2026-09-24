using System.Globalization;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Turns a finished case's <c>postProcessing/</c> tree into the response
/// row: for every requested response, the last row of every data file in
/// its latest time directory, each column a value named
/// <c>&lt;response&gt;.&lt;column&gt;</c> (with the file's name in between when
/// a function object writes more than one file). The time column is left out;
/// it is the same for every column and the result carries it once.
/// </summary>
public static class FoamResponseExtractor
{
    #region Constants

    private const string POST_PROCESSING = "postProcessing";

    private const string TIME_COLUMN = "Time";

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
}
