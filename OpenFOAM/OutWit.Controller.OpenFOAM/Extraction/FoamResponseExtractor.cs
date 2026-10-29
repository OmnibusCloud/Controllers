using System.Globalization;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Turns a finished case's <c>postProcessing/</c> tree into the response
/// row: for every requested response, the latest last row of every data
/// file over all its time directories (a response measured during the solve
/// writes under the start time, a post step under the latest written time;
/// the later row counts, and on a tie the solve's), each column a value named
/// <c>&lt;response&gt;.&lt;column&gt;</c> (with the file's name in between when
/// a function object writes more than one file; a vector or tensor value is
/// a column per component, <c>areaAverage(U)_x</c> ..., and a probe column
/// is named <c>&lt;field&gt;@probe&lt;N&gt;(x y z)</c> with its component
/// suffix, both by <see cref="FoamPostProcessingReader"/>; a probe column
/// carries the field itself). A
/// probe outside the mesh contributes nothing and a note names it. The time column is left out;
/// it is the same for every column and the result carries it once. Given the
/// run's final time, a file whose last row is earlier - a function object
/// that stopped writing before the run ended - is not reported: a stale value
/// is worse than none, and a note names it. Every such note is a
/// <see cref="FoamNoteKind.Warning"/> on each column left out, named as the
/// row would have named the value.
/// </summary>
public static class FoamResponseExtractor
{
    #region Constants

    private const string POST_PROCESSING = "postProcessing";

    private const string TIME_COLUMN = "Time";

    private const string NOTES_FILE = "log.responses";

    private const string DATA_EXTENSION = ".dat";

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
    /// <param name="notes">
    /// Receives a <see cref="FoamNoteKind.Warning"/> on every column left
    /// out, named as the row would have named its value: one sentence per
    /// stale file on each of its columns, one per probe outside the mesh on
    /// each of its components; null discards them.
    /// </param>
    /// <returns>The row; a response whose files are missing or stale contributes nothing.</returns>
    public static FoamResponseRowData Extract(string caseDirectory, FoamExtractionRequestData? request, double? finalTime, ICollection<FoamNote>? notes)
    {
        var row = new FoamResponseRowData();
        if (request == null)
            return row;

        foreach (var response in request.Responses)
        {
            // Every time directory is read: a response measured during the
            // solve writes under the start time, a post step under the
            // latest written time, and the row that counts is the latest
            // row of each file name wherever it lies - on a tie, the solve's
            // own (the earlier directory).
            var latest = LatestRows(Path.Combine(caseDirectory, POST_PROCESSING, response.Name));
            var files = latest.Keys.OrderBy(file => file, StringComparer.Ordinal).ToList();

            foreach (var file in files)
            {
                var table = latest[file];

                // A probe column carries its field already (p@probe0(0.1 0 0)).
                var stem = Path.GetFileNameWithoutExtension(file);
                var prefix = files.Count > 1 && !table.IsProbes ? $"{response.Name}.{stem}" : response.Name;

                if (finalTime is { } final && IsStale(table.Columns, table.Values, final, out var time))
                {
                    var sentence = string.Create(CultureInfo.InvariantCulture,
                        $"{response.Name}: the last row of {Path.GetFileName(file)} is at time {time}, the run ended at {final} - not reported.");
                    NoteStale(notes, table, prefix, sentence);
                    continue;
                }

                foreach (var note in table.Notes)
                {
                    notes?.Add(new FoamNote
                    {
                        Kind = note.Kind,
                        Column = note.Column == null ? null : $"{prefix}.{note.Column}",
                        Text = $"{response.Name}: {note.Text}"
                    });
                }

                for (var index = 0; index < table.Columns.Count; index++)
                {
                    var column = table.Columns[index];
                    if (index == 0 && string.Equals(column, TIME_COLUMN, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = table.Values[index];
                    if (double.IsNaN(value))
                        continue;

                    row.Values.Add(new FoamResponseValueData { Name = $"{prefix}.{column}", Value = value });
                }
            }
        }

        return row;
    }

    /// <summary>
    /// Writes the notes of a run beside the step logs (<c>log.responses</c>),
    /// where they travel with the logs: one line per sentence
    /// (<see cref="Sentences"/>); no notes, no file.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="notes">The notes, those of <see cref="Extract(string, FoamExtractionRequestData?, double?, ICollection{FoamNote}?)"/> among them.</param>
    public static void WriteNotes(string caseDirectory, IReadOnlyCollection<FoamNote> notes)
    {
        if (notes.Count == 0)
            return;

        File.WriteAllLines(Path.Combine(caseDirectory, NOTES_FILE), Sentences(notes));
    }

    /// <summary>
    /// The sentences of notes as <c>log.responses</c> carries them: each
    /// once, in order of first appearance - a sentence about several columns
    /// is one note per column, and one line.
    /// </summary>
    /// <param name="notes">The notes.</param>
    /// <returns>The sentences.</returns>
    public static IReadOnlyList<string> Sentences(IEnumerable<FoamNote> notes)
    {
        return notes.Select(note => note.Text).Distinct(StringComparer.Ordinal).ToList();
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

    /// <summary>
    /// The latest last row of every file name under a function object's
    /// output directory, over all its time directories: the row with the
    /// greatest time, and on a tie the one from the earliest directory (the
    /// solve's own measurement rather than a post step's).
    /// </summary>
    /// <param name="functionDirectory">The <c>postProcessing/&lt;name&gt;</c> directory.</param>
    /// <returns>By file name (<c>surfaceFieldValue.dat</c>, <c>p</c>), the table to report; empty when there is none.</returns>
    public static IReadOnlyDictionary<string, FoamPostProcessingTable> LatestRows(string functionDirectory)
    {
        var latest = new Dictionary<string, (double Time, FoamPostProcessingTable Table)>(StringComparer.Ordinal);
        if (!Directory.Exists(functionDirectory))
            return latest.ToDictionary(entry => entry.Key, entry => entry.Value.Table, StringComparer.Ordinal);

        var directories = Directory.EnumerateDirectories(functionDirectory)
            .Select(directory => (Directory: directory, Time: double.TryParse(Path.GetFileName(directory), NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ? time : double.NaN))
            .Where(entry => !double.IsNaN(entry.Time))
            .OrderBy(entry => entry.Time);

        foreach (var (directory, _) in directories)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var table = FoamPostProcessingReader.ReadLastRow(file);
                if (table == null)
                    continue;

                var time = TimeOf(table);
                var name = MeasurementName(file);
                if (!latest.TryGetValue(name, out var known) || time > known.Time)
                    latest[name] = (time, table);
            }
        }

        return latest.ToDictionary(entry => entry.Key, entry => entry.Value.Table, StringComparer.Ordinal);
    }

    #endregion

    #region Tools

    // The measurement a file belongs to: its name, with the "_<n>" a
    // writer appends when its file already exists in the start-time
    // directory folded away (measured on v2606, 2026-10-28: a post step after
    // the solve wrote surfaceFieldValue_0.dat beside surfaceFieldValue.dat).
    // A probe file (no extension) is never folded: a field may carry such a
    // name of its own.
    private static string MeasurementName(string file)
    {
        var name = Path.GetFileName(file);
        if (!name.EndsWith(DATA_EXTENSION, StringComparison.Ordinal))
            return name;

        var stem = name[..^DATA_EXTENSION.Length];
        var underscore = stem.LastIndexOf('_');
        return underscore > 0 && stem[(underscore + 1)..].All(char.IsAsciiDigit) ? stem[..underscore] + DATA_EXTENSION : name;
    }

    // The time of a table's row: its time column, or negative infinity when it has none.
    private static double TimeOf(FoamPostProcessingTable table)
    {
        return table.Columns.Count > 0 && table.Values.Count > 0 && string.Equals(table.Columns[0], TIME_COLUMN, StringComparison.OrdinalIgnoreCase)
            ? table.Values[0]
            : double.NegativeInfinity;
    }

    // A stale file's sentence on every column of a value - not the time
    // column, not a word such as fieldMinMax's field name, a probe outside
    // the mesh included - or, for a file with no such column, on the
    // response as a whole.
    private static void NoteStale(ICollection<FoamNote>? notes, FoamPostProcessingTable table, string prefix, string sentence)
    {
        if (notes == null)
            return;

        var leftOut = table.Notes.Select(note => note.Column).ToHashSet(StringComparer.Ordinal);
        var columns = table.Columns
            .Where((column, index) => (index > 0 || !string.Equals(column, TIME_COLUMN, StringComparison.OrdinalIgnoreCase))
                                      && (!double.IsNaN(table.Values[index]) || leftOut.Contains(column)))
            .ToList();

        if (columns.Count == 0)
            notes.Add(new FoamNote { Kind = FoamNoteKind.Warning, Text = sentence });

        foreach (var column in columns)
            notes.Add(new FoamNote { Kind = FoamNoteKind.Warning, Column = $"{prefix}.{column}", Text = sentence });
    }

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
