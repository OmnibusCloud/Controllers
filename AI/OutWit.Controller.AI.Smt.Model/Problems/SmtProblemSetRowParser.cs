using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// Reads one line of a problem set into its fields, strictly: a line is taken exactly when the
/// published schema of a row allows it, as far as a single field goes. A line that is not valid
/// UTF-8 or not one JSON object is no row at all. In a row, a name that is none of a row's
/// fields (a misspelt one is such a name), a field given twice, a value of another type (null
/// included), a number outside its range and a text that is not valid Unicode are noted by the
/// field's name; the first such fault of a line is the one kept, and the rest of the line is
/// still read for its id.
/// <para>
/// Nothing here throws for what a line holds, and no fault carries more of the line than the
/// name of a field cut to a length.
/// </para>
/// </summary>
internal static class SmtProblemSetRowParser
{
    #region Constants

    /// <summary>A field name longer than this is not read: no field is that long.</summary>
    private const int MAX_FIELD_NAME_BYTES = 256;

    /// <summary>The most bytes of JSON one character can take (an escape of four hexadecimal digits).</summary>
    private const int MAX_JSON_BYTES_PER_CHARACTER = 6;

    private const int MAX_DEPTH = 8;
    private const string NOT_AN_OBJECT = "the line is not one JSON object";

    private static readonly JsonReaderOptions READER_OPTIONS = new() { MaxDepth = MAX_DEPTH, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    private static readonly (byte[] Name, SmtProblemSetField Field)[] FIELDS =
    [
        ("id"u8.ToArray(), SmtProblemSetField.Id), ("smt2"u8.ToArray(), SmtProblemSetField.Smt2), ("query"u8.ToArray(), SmtProblemSetField.Query),
        ("values"u8.ToArray(), SmtProblemSetField.Values), ("project"u8.ToArray(), SmtProblemSetField.Project),
        ("max_models"u8.ToArray(), SmtProblemSetField.MaxModels), ("goal"u8.ToArray(), SmtProblemSetField.Goal),
        ("expected"u8.ToArray(), SmtProblemSetField.Expected), ("tags"u8.ToArray(), SmtProblemSetField.Tags)
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Reads the fields of one line.
    /// </summary>
    /// <param name="line">The line's bytes, without its line end.</param>
    /// <returns>What the line holds, and the first thing wrong with it.</returns>
    public static SmtProblemSetRowDraft Parse(ReadOnlySpan<byte> line)
    {
        if (!Utf8.IsValid(line))
            return Malformed("the line is not valid UTF-8");

        var draft = new SmtProblemSetRowDraft();
        var reader = new Utf8JsonReader(line, READER_OPTIONS);

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return Malformed(NOT_AN_OBJECT);

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var field = FieldOf(ref reader, draft);
                reader.Read();

                if (field == SmtProblemSetField.None || !Once(draft, field))
                    reader.Skip();
                else
                    ReadValue(ref reader, draft, field);
            }

            // Anything after the object is the reader's own error.
            reader.Read();
        }
        catch (JsonException)
        {
            return Malformed(NOT_AN_OBJECT);
        }

        return draft;
    }

    /// <summary>
    /// The name of a field as a row writes it.
    /// </summary>
    /// <param name="field">One field.</param>
    /// <returns>The name: <c>max_models</c>, <c>smt2</c>, ...</returns>
    public static string NameOf(SmtProblemSetField field)
    {
        foreach (var (name, candidate) in FIELDS)
        {
            if (candidate == field)
                return Encoding.ASCII.GetString(name);
        }

        return "";
    }

    private static SmtProblemSetRowDraft Malformed(string reason)
    {
        var draft = new SmtProblemSetRowDraft { IsMalformed = true };
        draft.Refuse(SmtRefusalCodes.ROW_MALFORMED, reason);
        return draft;
    }

    private static void ReadValue(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft, SmtProblemSetField field)
    {
        switch (field)
        {
            case SmtProblemSetField.Id:
                draft.Id = Id(ref reader, draft);
                break;
            case SmtProblemSetField.Smt2:
                draft.Smt2 = Text(ref reader, draft, "smt2");
                break;
            case SmtProblemSetField.Query:
                draft.Query = Query(ref reader, draft);
                break;
            case SmtProblemSetField.Values:
                draft.Values = Texts(ref reader, draft, "values", SmtProblemSetLimits.MAX_VALUES);
                break;
            case SmtProblemSetField.Project:
                draft.Project = Texts(ref reader, draft, "project", SmtProblemSetLimits.MAX_PROJECT);
                break;
            case SmtProblemSetField.MaxModels:
                draft.MaxModels = Integer(ref reader, draft, "max_models", 1, SmtOptionsRules.MAX_MODELS);
                break;
            case SmtProblemSetField.Goal:
                draft.Goal = Text(ref reader, draft, "goal");
                break;
            case SmtProblemSetField.Expected:
                draft.Expected = Expected(ref reader, draft);
                break;
            case SmtProblemSetField.Tags:
                draft.Tags = Tags(ref reader, draft);
                break;
        }
    }

    #endregion

    #region Values

    private static string? Id(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft)
    {
        if (!IsString(ref reader, draft, "id"))
            return null;

        // No id is longer than its bound, however it is escaped: a longer value is not made a string.
        var id = reader.ValueSpan.Length > SmtProblemSetLimits.MAX_ID_CHARACTERS * MAX_JSON_BYTES_PER_CHARACTER ? null : StringOf(ref reader);
        if (id != null && SmtProblemSetIds.IsId(id))
            return id;

        draft.Refuse(SmtRefusalCodes.ID_INVALID, $"'id' must be 1 to {SmtProblemSetLimits.MAX_ID_CHARACTERS} characters, each a letter A-Z or a-z, a digit, or one of . _ - / + : @ =");
        return null;
    }

    private static string? Text(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft, string name)
    {
        if (!IsString(ref reader, draft, name))
            return null;

        var text = StringOf(ref reader);
        if (text == null)
            draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}' is not valid Unicode text");

        return text;
    }

    private static SmtQuery? Query(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (Is(ref reader, "check"u8))
                return SmtQuery.Check;

            if (Is(ref reader, "enumerate"u8))
                return SmtQuery.Enumerate;

            if (Is(ref reader, "prove"u8))
                return SmtQuery.Prove;
        }

        draft.Refuse(SmtRefusalCodes.FIELD_INVALID, "'query' must be \"check\", \"enumerate\" or \"prove\"");
        reader.Skip();
        return null;
    }

    private static SmtProblemStatus? Expected(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (Is(ref reader, "sat"u8))
                return SmtProblemStatus.Sat;

            if (Is(ref reader, "unsat"u8))
                return SmtProblemStatus.Unsat;
        }

        draft.Refuse(SmtRefusalCodes.FIELD_INVALID, "'expected' must be \"sat\" or \"unsat\"");
        reader.Skip();
        return null;
    }

    private static string? Tags(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft)
    {
        if (!IsString(ref reader, draft, "tags"))
            return null;

        var tags = reader.ValueSpan.Length > SmtProblemSetLimits.MAX_TAGS_BYTES * MAX_JSON_BYTES_PER_CHARACTER ? null : StringOf(ref reader);
        if (tags != null && Encoding.UTF8.GetByteCount(tags) <= SmtProblemSetLimits.MAX_TAGS_BYTES && !tags.Any(char.IsControl))
            return tags;

        draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'tags' must be at most {SmtProblemSetLimits.MAX_TAGS_BYTES} bytes of UTF-8 without control characters");
        return null;
    }

    /// <summary>
    /// An array of strings, read to its end whatever it holds: one to so many items, each a text.
    /// </summary>
    private static List<string>? Texts(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft, string name, int most)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}' must be an array of strings");
            reader.Skip();
            return null;
        }

        var texts = new List<string>();
        var count = 0;
        var fine = true;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count >= most)
            {
                // Past the bound nothing is kept: the array is only passed to its end.
                draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}' must hold at most {most} items");
                reader.Skip();
                fine = false;
            }
            else if (reader.TokenType != JsonTokenType.String)
            {
                draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}[{count}]' must be a string");
                reader.Skip();
                fine = false;
            }
            else if (StringOf(ref reader) is { } text)
            {
                texts.Add(text);
            }
            else
            {
                draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}[{count}]' is not valid Unicode text");
                fine = false;
            }

            count++;
        }

        if (count == 0)
        {
            draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}' must hold at least one item");
            return null;
        }

        return fine ? texts : null;
    }

    /// <summary>An integer as a schema means one: a JSON number without a fractional part, within the field's range.</summary>
    private static int? Integer(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft, string name, int least, int most)
    {
        var range = $"'{name}' must be an integer from {least} to {most}";
        if (reader.TokenType != JsonTokenType.Number)
        {
            draft.Refuse(SmtRefusalCodes.FIELD_INVALID, range);
            reader.Skip();
            return null;
        }

        // 2.0 and 1e2 are integers to a schema; 1.5 and 1e400 are not.
        if (!reader.TryGetInt64(out var value))
        {
            if (!reader.TryGetDecimal(out var number) || number != decimal.Truncate(number) || number < least || number > most)
            {
                draft.Refuse(SmtRefusalCodes.FIELD_INVALID, range);
                return null;
            }

            value = (long)number;
        }

        if (value < least || value > most)
        {
            draft.Refuse(SmtRefusalCodes.FIELD_INVALID, range);
            return null;
        }

        return (int)value;
    }

    private static bool IsString(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft, string name)
    {
        if (reader.TokenType == JsonTokenType.String)
            return true;

        draft.Refuse(SmtRefusalCodes.FIELD_INVALID, $"'{name}' must be a string");
        reader.Skip();
        return false;
    }

    /// <summary>Whether a JSON string or name is a text; one with an escape that is no Unicode text is none.</summary>
    private static bool Is(ref Utf8JsonReader reader, ReadOnlySpan<byte> text)
    {
        try
        {
            return reader.ValueTextEquals(text);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The text of a JSON string; null when it is no Unicode text (an escape that is half of a surrogate pair).</summary>
    private static string? StringOf(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    #endregion

    #region Field Names

    private static SmtProblemSetField FieldOf(ref Utf8JsonReader reader, SmtProblemSetRowDraft draft)
    {
        foreach (var (name, field) in FIELDS)
        {
            if (Is(ref reader, name))
                return field;
        }

        var shown = reader.ValueSpan.Length > MAX_FIELD_NAME_BYTES ? null : StringOf(ref reader);
        draft.Refuse(SmtRefusalCodes.FIELD_NOT_ALLOWED, shown == null
            ? "a row has no field with such a name (the name is too long or no Unicode text to be shown)"
            : $"'{SmtRefusals.Token(shown)}' is no field of a row");

        return SmtProblemSetField.None;
    }

    /// <summary>
    /// Notes that a row gives a field. A field given twice is a fault of the row; an id given
    /// twice is no id of the row at all.
    /// </summary>
    private static bool Once(SmtProblemSetRowDraft draft, SmtProblemSetField field)
    {
        if (!draft.Has(field))
        {
            draft.Seen |= field;
            return true;
        }

        draft.Refuse(SmtRefusalCodes.ROW_MALFORMED, $"the field '{NameOf(field)}' is given twice");
        if (field == SmtProblemSetField.Id)
            draft.Id = null;

        return false;
    }

    #endregion
}
