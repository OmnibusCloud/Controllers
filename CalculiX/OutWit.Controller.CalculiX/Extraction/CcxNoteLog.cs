using System.Text;

namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// Carries the notes of a variant inside <c>CcxResultData.LogTail</c>, so the
/// shipped wire layout of the result stays as it is (every installed client
/// reads the Sweep manifest in default-mode MemoryPack, which checks the
/// member count both ways). The solver's own tail comes first, then the line
/// <c>--- notes ---</c>, then one line per note:
/// <c>[note]&lt;TAB&gt;KIND&lt;TAB&gt;column&lt;TAB&gt;text</c> with KIND one of
/// INFO, WARNING, ERROR, the column empty for a note about the variant as a
/// whole, and no tab or line break inside column or text. A client that
/// shows the tail of a failed row as it is still shows the notes; a client
/// that knows the grammar splits each note line on the tab, four fields.
/// </summary>
public static class CcxNoteLog
{
    #region Constants

    /// <summary>The first field of a note line.</summary>
    public const string NOTE_PREFIX = "[note]";

    /// <summary>The line between the solver's tail and the notes.</summary>
    public const string SEPARATOR = "--- notes ---";

    private const char FIELD_SEPARATOR = '\t';

    private const int FIELDS = 4;

    #endregion

    #region Functions

    /// <summary>
    /// Composes the log tail of a variant.
    /// </summary>
    /// <param name="solverTail">The solver's tail; null or empty when the solve left none worth keeping.</param>
    /// <param name="notes">The variant's notes.</param>
    /// <returns>The tail as it was when there are no notes (null stays null); otherwise the tail, the separator and the note lines.</returns>
    public static string? Compose(string? solverTail, IReadOnlyList<CcxNote> notes)
    {
        if (notes.Count == 0)
            return solverTail;

        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(solverTail))
            builder.Append(solverTail.TrimEnd('\n', '\r')).Append('\n');

        builder.Append(SEPARATOR);
        foreach (var note in notes)
            builder.Append('\n').Append(Line(note));

        return builder.ToString();
    }

    /// <summary>
    /// One note as a note line.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns><c>[note]&lt;TAB&gt;KIND&lt;TAB&gt;column&lt;TAB&gt;text</c>.</returns>
    public static string Line(CcxNote note)
    {
        return string.Join(FIELD_SEPARATOR, NOTE_PREFIX, note.Kind.ToString().ToUpperInvariant(), Clean(note.Column), Clean(note.Text));
    }

    /// <summary>
    /// Reads a log tail back: the solver's tail and the notes.
    /// </summary>
    /// <param name="logTail">The tail as the result carries it.</param>
    /// <returns>The solver's tail (null when there is none) and the notes (empty when there are none).</returns>
    public static (string? SolverTail, List<CcxNote> Notes) Parse(string? logTail)
    {
        if (string.IsNullOrEmpty(logTail))
            return (null, []);

        var lines = logTail.Split('\n');
        var separator = Array.IndexOf(lines, SEPARATOR);
        if (separator < 0)
            return (logTail, []);

        var tail = separator == 0 ? null : string.Join('\n', lines, 0, separator);
        var notes = new List<CcxNote>();

        for (var i = separator + 1; i < lines.Length; i++)
        {
            var fields = lines[i].TrimEnd('\r').Split(FIELD_SEPARATOR, FIELDS);
            if (fields.Length != FIELDS || fields[0] != NOTE_PREFIX)
                continue;

            notes.Add(new CcxNote
            {
                Kind = Enum.TryParse<CcxNoteKind>(fields[1], ignoreCase: true, out var kind) ? kind : CcxNoteKind.Info,
                Column = fields[2].Length == 0 ? null : fields[2],
                Text = fields[3]
            });
        }

        return (tail, notes);
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    #endregion
}
