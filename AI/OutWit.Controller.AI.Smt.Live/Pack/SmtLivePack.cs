using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Pack;

/// <summary>
/// <c>pack</c>: a folder of SMT-LIB files (<c>*.smt2</c>, in its subfolders too, in the ordinal
/// order of their paths) into a problem set, one row a file, its id made of its path
/// (<see cref="SmtLiveProblemIds"/>). The text goes into the row as it is; <c>expected</c> is not
/// written: the controller reads it from the problem's <c>:status</c>. With a query, the rows
/// name it and carry what it needs: <c>enumerate</c> projects on every constant the problem
/// declares; <c>prove</c> takes the problem's last <c>assert</c> as the goal and leaves it out of
/// the problem. The set is then read back by the model package's reader, the code the server
/// reads it with, and every row it refuses is printed as the server would refuse it.
/// </summary>
internal sealed class SmtLivePack
{
    #region Constants

    private const string PATTERN = "*" + SmtLiveProblemIds.EXTENSION;
    private const string ASSERT = "assert";
    private const int SHOWN_REFUSALS = 50;
    private const byte LINE_FEED = (byte)'\n';

    private static readonly UTF8Encoding STRICT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonWriterOptions WRITER = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    #endregion

    #region Fields

    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="output">Where the lines go.</param>
    public SmtLivePack(TextWriter output)
    {
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Packs a folder.
    /// </summary>
    /// <param name="folder">The folder of SMT-LIB files.</param>
    /// <param name="query">The query every row names; null to name none (a row then takes the run's).</param>
    /// <param name="path">The problem set to write; replaced when it is there.</param>
    /// <returns>The exit code: 0 when every file became a row the reader takes, 1 when a file was skipped or a row is refused (the set is written all the same), 2 when there is nothing to pack.</returns>
    public int Run(string folder, SmtQuery? query, string path)
    {
        if (!Directory.Exists(folder))
        {
            m_output.WriteLine($"{folder} is not a folder");
            return SmtLiveExitCodes.USAGE;
        }

        var files = Directory.EnumerateFiles(folder, PATTERN, SearchOption.AllDirectories)
            .Select(file => (Path: file, Relative: Path.GetRelativePath(folder, file).Replace('\\', '/')))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            m_output.WriteLine($"{folder} holds no {PATTERN} file");
            return SmtLiveExitCodes.USAGE;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory != null)
            Directory.CreateDirectory(directory);

        var ids = new SmtLiveProblemIds();
        var notes = new Dictionary<int, string>();
        var rows = 0;
        var skipped = 0;
        using (var stream = File.Create(path))
        {
            foreach (var file in files)
            {
                var text = Read(file.Path, file.Relative);
                if (text == null)
                {
                    skipped++;
                    continue;
                }

                var note = WriteRow(stream, ids.Of(file.Relative), text, query);
                if (note != null)
                    notes[rows] = note;

                rows++;
            }
        }

        m_output.WriteLine($"pack     : {files.Count} file(s) -> {path}: {rows} row(s){(skipped > 0 ? $", {skipped} file(s) skipped" : "")}" +
                           $"{(query == null ? ", no query named (a row takes the run's)" : $", query {SmtLiveArguments.WordOf(query.Value)}")}");

        var refused = Check(path, query, notes);
        return refused == 0 && skipped == 0 ? SmtLiveExitCodes.OK : SmtLiveExitCodes.FAILED;
    }

    /// <summary>The text of a file; null, with the reason printed, when no row can hold it.</summary>
    private string? Read(string path, string relative)
    {
        var size = new FileInfo(path).Length;
        if (size > SmtProblemSetLimits.MAX_LINE_BYTES)
        {
            m_output.WriteLine($"skipped  : {relative}: {size} bytes, more than the {SmtProblemSetLimits.MAX_LINE_BYTES} a row of a set may have (the server would refuse it as row_too_long)");
            return null;
        }

        try
        {
            return STRICT.GetString(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException)
        {
            m_output.WriteLine($"skipped  : {relative}: not valid UTF-8, and a row of a set holds a problem as text");
            return null;
        }
    }

    /// <summary>Writes one row; returns why it lacks a field its query needs, or null.</summary>
    private static string? WriteRow(Stream stream, string id, string text, SmtQuery? query)
    {
        string? note;
        using (var writer = new Utf8JsonWriter(stream, WRITER))
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            if (query != null)
                writer.WriteString("query", SmtLiveArguments.WordOf(query.Value));

            note = query switch
            {
                SmtQuery.Enumerate => WriteEnumerate(writer, text),
                SmtQuery.Prove => WriteProve(writer, text),
                _ => WriteCheck(writer, text)
            };

            writer.WriteEndObject();
        }

        stream.WriteByte(LINE_FEED);
        return note;
    }

    private static string? WriteCheck(Utf8JsonWriter writer, string text)
    {
        writer.WriteString("smt2", text);
        return null;
    }

    private static string? WriteEnumerate(Utf8JsonWriter writer, string text)
    {
        writer.WriteString("smt2", text);

        var sanitized = SmtSanitizer.Sanitize(text);
        if (!sanitized.IsAccepted)
            return Note(sanitized.Refusal);

        var constants = sanitized.Problem.Constants;
        if (constants.Count == 0)
            return "the problem declares no constant to project on";

        writer.WriteStartArray("project");
        foreach (var constant in constants)
            writer.WriteStringValue(constant.Symbol.ToString());
        writer.WriteEndArray();

        return constants.Count > SmtProblemSetLimits.MAX_PROJECT
            ? $"the problem declares {constants.Count} constants; a row projects on at most {SmtProblemSetLimits.MAX_PROJECT}"
            : null;
    }

    private static string? WriteProve(Utf8JsonWriter writer, string text)
    {
        var sanitized = SmtSanitizer.Sanitize(text);
        if (!sanitized.IsAccepted)
        {
            writer.WriteString("smt2", text);
            return Note(sanitized.Refusal);
        }

        var commands = sanitized.Problem.Commands;
        var goal = -1;
        for (var index = 0; index < commands.Count; index++)
        {
            if (commands[index].Head == ASSERT && commands[index].Items.Count == 2)
                goal = index;
        }

        if (goal < 0)
        {
            writer.WriteString("smt2", text);
            return "the problem has no assert to take as the goal";
        }

        var premises = new StringBuilder();
        for (var index = 0; index < commands.Count; index++)
        {
            if (index != goal)
                premises.Append(commands[index]).Append('\n');
        }

        writer.WriteString("smt2", premises.ToString());
        writer.WriteString("goal", commands[goal].Items[1].ToString());
        return null;
    }

    private static string Note(SmtRefusalData? refusal)
    {
        return refusal == null ? "the sanitizer refuses the problem" : $"the sanitizer refuses the problem: {refusal.Code}, line {refusal.Line}: {refusal.Reason}";
    }

    /// <summary>Reads the set back as the server reads it and prints what it refuses; returns the refused rows.</summary>
    private int Check(string path, SmtQuery? query, IReadOnlyDictionary<int, string> notes)
    {
        using var stream = File.OpenRead(path);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData { Query = query ?? SmtQuery.Check });
        var shown = 0;
        while (reader.TryRead(out var row))
        {
            if (row.IsAccepted || row.Refusal == null || shown++ >= SHOWN_REFUSALS)
                continue;

            var refusal = row.Refusal;
            var line = refusal.Line > 0 ? $", line {refusal.Line}" : "";
            var command = refusal.Command.Length > 0 ? $" ({refusal.Command})" : "";
            var note = notes.TryGetValue(row.Index, out var why) ? $" [{why}]" : "";
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}refused  : row {row.Line} ({refusal.File}){line}{command}: {refusal.Code}: {refusal.Reason}{note}");
        }

        var summary = reader.Summary;
        if (summary.Refused > SHOWN_REFUSALS)
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}refused  : and {summary.Refused - SHOWN_REFUSALS} more");

        var codes = summary.RefusalCodes.Count == 0 ? "" : $" ({string.Join(", ", summary.RefusalCodes.Select(pair => $"{pair.Key} {pair.Value}"))})";
        m_output.WriteLine($"checked  : {summary.Rows} row(s) read as the server reads them: {summary.Accepted} taken, {summary.Refused} refused{codes}");
        m_output.WriteLine($"logics   : {Counts(summary.Logics.Select(pair => (pair.Key, pair.Value)))}{(summary.OtherLogics > 0 ? $", {summary.OtherLogics} of other logics" : "")}");
        m_output.WriteLine($"queries  : {Counts(summary.Queries.Select(pair => (SmtLiveArguments.WordOf(pair.Key), pair.Value)))}");
        m_output.WriteLine($"expected : {Counts(summary.Expected.Select(pair => (pair.Key.ToString().ToLowerInvariant(), pair.Value)))} (from the problems' :status)");
        m_output.WriteLine($"stripped : {Counts(summary.Stripped.Select(pair => (pair.Key, pair.Value)))} (left out by the sanitizer: the query decides what runs)");
        return summary.Refused;
    }

    private static string Counts(IEnumerable<(string Name, int Count)> counts)
    {
        var shown = counts.Select(count => $"{(count.Name.Length == 0 ? "(none)" : count.Name)} {count.Count}").ToList();
        return shown.Count == 0 ? "-" : string.Join(", ", shown);
    }

    #endregion
}
