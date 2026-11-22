using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Parses a taskset blob into tasks. The wire format is JSON-lines — one task per
/// non-blank line — the pipeline-native shape a lab produces from its own tooling:
///
///   {"index":0,"runtime":"python-3.14.6","entry":"main.py",
///    "sources":{"main.py":"print(input())"},"args":[],"stdin":"hi","seed":0,
///    "suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}],
///    "limits":{"fuel":5000000000,"memory":268435456,
///              "stdout_bytes":262144,"stderr_bytes":65536}}
///
/// `index` defaults to the line ordinal; `entry` defaults to the first source; `suite`
/// and `limits` are optional. For the raw WASI runtime (`"runtime":"wasi"`) the entry
/// source is the module's bytes in base64.
/// <para>
/// A line is accepted exactly when the published schema (docs/verify-taskset.schema.json)
/// allows it, and refused by name otherwise: a field the schema does not have (a misspelt
/// <c>expected_stdout</c> must never turn a test into a pass), a field given twice, a value of
/// another type (a suite that is not an array, a limit that is not an integer, a null), a
/// number outside its range, a NUL in a source or an argument, text that is not valid Unicode,
/// more sources, arguments or cases than a task may have, more text than a task may have. A
/// line is read in one pass over its bytes and no string is made of text a task may not have,
/// so a hostile line costs no more memory than its own bytes.
/// </para>
/// <para>
/// A problem is a <see cref="FormatException"/> whose words are fixed, bounded and name what
/// is wrong; nothing of the line travels in it but names cut to a length and to plain
/// characters.
/// </para>
/// </summary>
public static class VerifyTasksetParser
{
    #region Constants

    /// <summary>The most characters of a lab's own name a message shows.</summary>
    private const int MAX_SHOWN_NAME = 64;

    /// <summary>A field name longer than this is not read: no field is that long.</summary>
    private const int MAX_FIELD_NAME_BYTES = 256;

    /// <summary>The most bytes of JSON one character can take (an escape of four hex digits).</summary>
    private const int MAX_JSON_BYTES_PER_CHARACTER = 6;

    private const int MAX_DEPTH = 8;

    /// <summary>What the line of a list of errors calls the errors it does not show.</summary>
    public const string MALFORMED_LINES = "malformed line(s)";

    private static readonly JsonReaderOptions READER_OPTIONS = new() { MaxDepth = MAX_DEPTH, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    private static readonly (byte[] Name, VerifyTaskField Field)[] TASK_FIELDS =
    [
        ("index"u8.ToArray(), VerifyTaskField.Index), ("runtime"u8.ToArray(), VerifyTaskField.Runtime), ("entry"u8.ToArray(), VerifyTaskField.Entry),
        ("sources"u8.ToArray(), VerifyTaskField.Sources), ("args"u8.ToArray(), VerifyTaskField.Args), ("stdin"u8.ToArray(), VerifyTaskField.Stdin),
        ("seed"u8.ToArray(), VerifyTaskField.Seed), ("suite"u8.ToArray(), VerifyTaskField.Suite), ("limits"u8.ToArray(), VerifyTaskField.Limits)
    ];

    private static readonly (byte[] Name, VerifyTaskField Field)[] CHECKER_FIELDS =
    [
        ("runtime"u8.ToArray(), VerifyTaskField.Runtime), ("entry"u8.ToArray(), VerifyTaskField.Entry), ("sources"u8.ToArray(), VerifyTaskField.Sources),
        ("args"u8.ToArray(), VerifyTaskField.Args), ("limits"u8.ToArray(), VerifyTaskField.Limits),
        ("expected_stdout"u8.ToArray(), VerifyTaskField.ExpectedStdout), ("expected_exit"u8.ToArray(), VerifyTaskField.ExpectedExit)
    ];

    private static readonly (byte[] Name, VerifyTaskField Field)[] CASE_FIELDS =
    [
        ("stdin"u8.ToArray(), VerifyTaskField.Stdin), ("args"u8.ToArray(), VerifyTaskField.Args),
        ("expected_stdout"u8.ToArray(), VerifyTaskField.ExpectedStdout), ("expected_exit"u8.ToArray(), VerifyTaskField.ExpectedExit)
    ];

    private static readonly (byte[] Name, VerifyTaskField Field)[] LIMIT_FIELDS =
    [
        ("fuel"u8.ToArray(), VerifyTaskField.Fuel), ("memory"u8.ToArray(), VerifyTaskField.Memory), ("wall_ms"u8.ToArray(), VerifyTaskField.WallMs),
        ("stdout_bytes"u8.ToArray(), VerifyTaskField.StdoutBytes), ("stderr_bytes"u8.ToArray(), VerifyTaskField.StderrBytes)
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Parses a taskset held in memory (JSON lines) into tasks, collecting one error per
    /// malformed line instead of throwing. For documents that are small by their nature and for
    /// tests: a taskset blob is read as a stream (<see cref="VerifyTasksetScanner"/>).
    /// </summary>
    /// <param name="jsonl">The taskset text.</param>
    /// <returns>The tasks that parsed and the errors, bounded in number and length.</returns>
    public static VerifyTasksetParseResult Parse(string jsonl)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl), writable: false);
        return Parse(stream);
    }

    /// <summary>
    /// Parses a taskset from a stream into tasks held in memory, collecting one error per
    /// malformed line instead of throwing.
    /// </summary>
    /// <param name="taskset">The taskset, at its start.</param>
    /// <returns>The tasks that parsed and the errors, bounded in number and length.</returns>
    public static VerifyTasksetParseResult Parse(Stream taskset)
    {
        var tasks = new List<VerifyTaskData>();
        var errors = new VerifyMessageList();
        var lines = new VerifyTasksetLines(taskset);
        var indices = new VerifyTaskIndices();

        while (lines.MoveNext())
        {
            try
            {
                tasks.Add(ParseLine(lines, indices));
            }
            catch (FormatException exception)
            {
                errors.Add($"line {lines.Number}: {exception.Message}");
            }
        }

        return new VerifyTasksetParseResult(tasks, errors.ToList(MALFORMED_LINES), errors.Count);
    }

    /// <summary>
    /// Parses the current line of a taskset and holds its index to the taskset's: one task an
    /// index, and no more tasks than a taskset may have.
    /// </summary>
    /// <param name="lines">The taskset's lines, on a line.</param>
    /// <param name="indices">The indices of the tasks before it; the task's own is added.</param>
    /// <returns>The task.</returns>
    /// <exception cref="FormatException">The line is not a task the schema allows; the message names what is wrong.</exception>
    public static VerifyTaskData ParseLine(VerifyTasksetLines lines, VerifyTaskIndices indices)
    {
        if (lines.TooLong)
            throw new FormatException($"the line is longer than {VerifyTasksetLimits.MAX_LINE_BYTES} bytes");

        if (indices.Count >= VerifyTasksetLimits.MAX_TASKS)
            throw new FormatException($"the taskset has more than {VerifyTasksetLimits.MAX_TASKS} tasks");

        var task = ParseTask(lines.Line.Span, indices.Count);

        // Indices key the report and pair the sampled replicas: one task per index.
        if (!indices.Add(task.TaskIndex, lines.Number, out var earlier))
            throw new FormatException($"duplicate 'index' {task.TaskIndex} (already used on {(earlier > 0 ? $"line {earlier}" : "an earlier line")})");

        return task;
    }

    /// <summary>Parses one task from the bytes of its line.</summary>
    /// <param name="utf8">The line: one JSON object.</param>
    /// <param name="ordinal">The index the task has when it names none.</param>
    /// <returns>The task.</returns>
    /// <exception cref="FormatException">The line is not a task the schema allows; the message names what is wrong.</exception>
    public static VerifyTaskData ParseTask(ReadOnlySpan<byte> utf8, int ordinal)
    {
        var draft = Read(utf8, checker: false);

        return new VerifyTaskData
        {
            TaskIndex = draft.Index ?? ordinal,
            RuntimeId = draft.Runtime!,
            Sources = draft.Sources!,
            EntryPoint = draft.Entry!,
            Args = draft.Args ?? [],
            Stdin = draft.Stdin,
            RandomSeed = draft.Seed ?? 0,
            Suite = draft.Suite is { Count: > 0 } ? new VerifySuiteData { Cases = draft.Suite } : null,
            Limits = draft.Limits
        };
    }

    /// <summary>
    /// Parses a checker spec: one JSON object with a task's runtime, entry, sources, arguments
    /// and limits, and the verdict rule beside them (docs/verify-checker.schema.json).
    /// </summary>
    /// <param name="utf8">The document: one JSON object.</param>
    /// <returns>The spec.</returns>
    /// <exception cref="FormatException">The document is not a checker the schema allows; the message names what is wrong.</exception>
    public static VerifyCheckerSpec ParseChecker(ReadOnlySpan<byte> utf8)
    {
        var draft = Read(utf8, checker: true);

        return new VerifyCheckerSpec(draft.Runtime!, draft.Entry!, draft.Sources!, draft.Args ?? [], draft.ExpectedStdout, draft.ExpectedExit ?? 0, draft.Limits);
    }

    #endregion

    #region Reading

    private static VerifyTaskDraft Read(ReadOnlySpan<byte> utf8, bool checker)
    {
        var draft = new VerifyTaskDraft();
        var reader = new Utf8JsonReader(utf8, READER_OPTIONS);

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new FormatException(checker ? "a checker spec must be one JSON object" : "a task must be one JSON object");

            var seen = VerifyTaskField.None;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var field = FieldOf(ref reader, checker ? CHECKER_FIELDS : TASK_FIELDS, "");
                Once(ref seen, field);
                reader.Read();

                switch (field)
                {
                    case VerifyTaskField.Index:
                        draft.Index = (int)Integer(ref reader, "index", 0, int.MaxValue);
                        break;
                    case VerifyTaskField.Runtime:
                        draft.Runtime = Text(ref reader, draft, "runtime", nulAllowed: false);
                        break;
                    case VerifyTaskField.Entry:
                        draft.Entry = Text(ref reader, draft, "entry", nulAllowed: false);
                        break;
                    case VerifyTaskField.Sources:
                        draft.Sources = Sources(ref reader, draft);
                        break;
                    case VerifyTaskField.Args:
                        draft.Args = Arguments(ref reader, draft, "args");
                        break;
                    case VerifyTaskField.Stdin:
                        draft.Stdin = Text(ref reader, draft, "stdin", nulAllowed: true);
                        break;
                    case VerifyTaskField.Seed:
                        draft.Seed = Integer(ref reader, "seed", long.MinValue, long.MaxValue);
                        break;
                    case VerifyTaskField.Suite:
                        draft.Suite = Suite(ref reader, draft);
                        break;
                    case VerifyTaskField.Limits:
                        draft.Limits = Limits(ref reader);
                        break;
                    case VerifyTaskField.ExpectedStdout:
                        draft.ExpectedStdout = Text(ref reader, draft, "expected_stdout", nulAllowed: true);
                        break;
                    case VerifyTaskField.ExpectedExit:
                        draft.ExpectedExit = (int)Integer(ref reader, "expected_exit", int.MinValue, int.MaxValue);
                        break;
                }
            }

            // Anything after the object is the reader's own error (caught below).
            reader.Read();
        }
        catch (JsonException exception)
        {
            throw new FormatException($"not valid JSON (at byte {exception.BytePositionInLine ?? 0})");
        }

        if (draft.Over)
            throw new FormatException($"the task's text is longer than {VerifyPackagePlanner.MAX_PACKAGE_BYTES} characters, the most a task may have");

        Complete(draft);
        return draft;
    }

    /// <summary>What a task needs besides fields that parse: a runtime, sources with portable names, an entry among them.</summary>
    private static void Complete(VerifyTaskDraft draft)
    {
        if (draft.Runtime == null)
            throw new FormatException("missing 'runtime'");

        if (draft.Sources == null || draft.Sources.Count == 0)
            throw new FormatException("missing or empty 'sources'");

        // A name must mean the same file on every node: the portable set only, whatever the runtime.
        var nameProblem = VerifySourceNames.Problem(draft.Sources);
        if (nameProblem != null)
            throw new FormatException(nameProblem);

        draft.Entry ??= draft.Sources[0].Name;
        var entry = draft.Sources.FirstOrDefault(source => source.Name == draft.Entry);
        if (entry == null)
            throw new FormatException($"'entry' '{Shown(draft.Entry)}' is not among the sources");

        // A task-supplied runtime (wasi) carries its module in the entry, base64; say so
        // here rather than as a CompileError on a node.
        if (VerifyRuntimeCatalog.Find(draft.Runtime)?.TaskSupplied == true && !IsBase64(entry.Content))
            throw new FormatException($"'entry' '{Shown(draft.Entry)}' of a '{Shown(draft.Runtime)}' task must hold the module bytes in base64");
    }

    private static List<VerifySourceFileData> Sources(ref Utf8JsonReader reader, VerifyTaskDraft draft)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new FormatException("'sources' must be an object of file name and content");

        var sources = new List<VerifySourceFileData>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (sources.Count >= VerifyTasksetLimits.MAX_SOURCES)
                throw new FormatException($"'sources' has more than {VerifyTasksetLimits.MAX_SOURCES} files");

            // No portable name is longer than its bound, however it is escaped.
            if (reader.ValueSpan.Length > VerifySourceNames.MAX_NAME_BYTES * MAX_JSON_BYTES_PER_CHARACTER)
                throw new FormatException($"a source name is longer than {VerifySourceNames.MAX_NAME_BYTES} bytes");

            var name = String(ref reader, "a source name");
            if (!names.Add(name))
                throw new FormatException($"source name '{Shown(name)}' is given twice");

            draft.Count(name.Length);
            reader.Read();
            sources.Add(new VerifySourceFileData { Name = name, Content = Text(ref reader, draft, "a source", nulAllowed: false) });
        }

        return sources;
    }

    private static List<string> Arguments(ref Utf8JsonReader reader, VerifyTaskDraft draft, string what)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new FormatException($"'{what}' must be an array of strings");

        var arguments = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (arguments.Count >= VerifyTasksetLimits.MAX_ARGS)
                throw new FormatException($"'{what}' has more than {VerifyTasksetLimits.MAX_ARGS} arguments");

            arguments.Add(Text(ref reader, draft, $"{what}[{arguments.Count}]", nulAllowed: false));
        }

        return arguments;
    }

    private static List<VerifySuiteCaseData> Suite(ref Utf8JsonReader reader, VerifyTaskDraft draft)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new FormatException("'suite' must be an array of cases");

        var cases = new List<VerifySuiteCaseData>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (cases.Count >= VerifyTasksetLimits.MAX_SUITE_CASES)
                throw new FormatException($"'suite' has more than {VerifyTasksetLimits.MAX_SUITE_CASES} cases");

            var place = $"suite[{cases.Count}]";
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new FormatException($"'{place}' must be an object");

            var item = new VerifySuiteCaseData();
            var seen = VerifyTaskField.None;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var field = FieldOf(ref reader, CASE_FIELDS, place + ".");
                Once(ref seen, field, place + ".");
                reader.Read();

                switch (field)
                {
                    case VerifyTaskField.Stdin:
                        item.Stdin = Text(ref reader, draft, $"{place}.stdin", nulAllowed: true);
                        break;
                    case VerifyTaskField.Args:
                        item.Args = Arguments(ref reader, draft, $"{place}.args");
                        break;
                    case VerifyTaskField.ExpectedStdout:
                        item.ExpectedStdout = Text(ref reader, draft, $"{place}.expected_stdout", nulAllowed: true);
                        break;
                    case VerifyTaskField.ExpectedExit:
                        item.ExpectedExitCode = (int)Integer(ref reader, $"{place}.expected_exit", int.MinValue, int.MaxValue);
                        break;
                }
            }

            cases.Add(item);
        }

        return cases;
    }

    private static VerifyLimitsData Limits(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new FormatException("'limits' must be an object");

        var limits = new VerifyLimitsData();
        var seen = VerifyTaskField.None;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = FieldOf(ref reader, LIMIT_FIELDS, "limits.");
            Once(ref seen, field, "limits.");
            reader.Read();

            switch (field)
            {
                case VerifyTaskField.Fuel:
                    limits.FuelBudget = Integer(ref reader, "limits.fuel", 1, long.MaxValue);
                    break;
                case VerifyTaskField.Memory:
                    limits.MemoryBytes = Integer(ref reader, "limits.memory", 1, long.MaxValue);
                    break;
                case VerifyTaskField.WallMs:
                    limits.WallTimeMs = (int)Integer(ref reader, "limits.wall_ms", 1, int.MaxValue);
                    break;
                case VerifyTaskField.StdoutBytes:
                    limits.StdoutLimitBytes = (int)Integer(ref reader, "limits.stdout_bytes", 1, int.MaxValue);
                    break;
                case VerifyTaskField.StderrBytes:
                    limits.StderrLimitBytes = (int)Integer(ref reader, "limits.stderr_bytes", 1, int.MaxValue);
                    break;
            }
        }

        return limits;
    }

    #endregion

    #region Values

    /// <summary>
    /// A string of a task's text, counted against what a task may have. Past that the string is
    /// not made: it is counted and passed over, and the task is refused when its line ends.
    /// </summary>
    private static string Text(ref Utf8JsonReader reader, VerifyTaskDraft draft, string what, bool nulAllowed)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new FormatException($"'{what}' must be a string");

        // A character is at least one byte of JSON, so the bytes bound the characters from
        // above; an unescaped value is counted exactly without being made.
        var bytes = reader.ValueSpan.Length;
        if (draft.Over
            || (!reader.ValueIsEscaped && draft.Characters + Encoding.UTF8.GetCharCount(reader.ValueSpan) > VerifyPackagePlanner.MAX_PACKAGE_BYTES)
            || bytes > MAX_JSON_BYTES_PER_CHARACTER * (VerifyPackagePlanner.MAX_PACKAGE_BYTES - draft.Characters + 1))
        {
            draft.Count(VerifyPackagePlanner.MAX_PACKAGE_BYTES + 1);
            return "";
        }

        var text = String(ref reader, $"'{what}'");
        if (!nulAllowed && text.Contains('\0'))
            throw new FormatException($"'{what}' holds a NUL character");

        draft.Count(text.Length);
        return text;
    }

    private static string String(ref Utf8JsonReader reader, string what)
    {
        try
        {
            return reader.GetString() ?? "";
        }
        catch (InvalidOperationException)
        {
            // An escape that is half of a surrogate pair, or bytes that are not UTF-8.
            throw new FormatException($"{what} is not valid Unicode text");
        }
    }

    /// <summary>An integer as the schema means one: a JSON number without a fractional part, within the field's range.</summary>
    private static long Integer(ref Utf8JsonReader reader, string what, long minimum, long maximum)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new FormatException($"'{what}' must be an integer");

        if (!reader.TryGetInt64(out var value))
        {
            // 1.0 and 1e2 are integers to a schema; 1.5 and 1e400 are not.
            if (!reader.TryGetDecimal(out var number) || number != decimal.Truncate(number))
                throw new FormatException($"'{what}' must be an integer");

            if (number < long.MinValue || number > long.MaxValue)
                throw new FormatException(OutOfRange(what, number < 0, minimum, maximum));

            value = (long)number;
        }

        if (value < minimum || value > maximum)
            throw new FormatException(OutOfRange(what, value < minimum, minimum, maximum));

        return value;
    }

    private static string OutOfRange(string what, bool below, long minimum, long maximum)
    {
        return below ? $"'{what}' must be {minimum} or more" : $"'{what}' must be {maximum} or less";
    }

    private static bool IsBase64(string content)
    {
        return content.Length > 0 && Convert.TryFromBase64String(content, new byte[content.Length], out _);
    }

    #endregion

    #region Field Names

    private static VerifyTaskField FieldOf(ref Utf8JsonReader reader, (byte[] Name, VerifyTaskField Field)[] fields, string place)
    {
        foreach (var (name, field) in fields)
        {
            if (reader.ValueTextEquals(name))
                return field;
        }

        if (reader.ValueSpan.Length > MAX_FIELD_NAME_BYTES)
            throw new FormatException($"unknown field in '{(place.Length == 0 ? "the task" : place.TrimEnd('.'))}' (its name is longer than {MAX_FIELD_NAME_BYTES} bytes)");

        throw new FormatException($"unknown field '{place}{Shown(String(ref reader, "a field name"))}'");
    }

    private static void Once(ref VerifyTaskField seen, VerifyTaskField field, string place = "")
    {
        if ((seen & field) != 0)
            throw new FormatException($"field '{place}{NameOf(field)}' is given twice");

        seen |= field;
    }

    private static string NameOf(VerifyTaskField field)
    {
        foreach (var fields in new[] { TASK_FIELDS, CHECKER_FIELDS, LIMIT_FIELDS })
        {
            foreach (var (name, candidate) in fields)
            {
                if (candidate == field)
                    return Encoding.ASCII.GetString(name);
            }
        }

        return field.ToString();
    }

    /// <summary>A name of the lab's own as a message may show it: cut to a length, and only plain characters.</summary>
    private static string Shown(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        var shown = name.Length > MAX_SHOWN_NAME ? name[..MAX_SHOWN_NAME] : name;
        return string.Concat(shown.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+' or '/' ? character : '?'));
    }

    #endregion
}
