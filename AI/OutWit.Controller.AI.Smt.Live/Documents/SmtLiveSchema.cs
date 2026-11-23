using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// A JSON Schema (draft 2020-12) of the kind the controller publishes for its documents, checked
/// for the keywords those schemas use and for no other. A schema that names a keyword this check
/// does not know is refused when it is loaded, so a document is never held to half of its
/// schema. What is wrong with a value is said with the JSON path of the place.
/// </summary>
internal sealed class SmtLiveSchema
{
    #region Constants

    private const int SHOWN_CHARACTERS = 48;

    private static readonly HashSet<string> ANNOTATIONS = new(StringComparer.Ordinal) { "$schema", "$id", "title", "description" };

    private static readonly HashSet<string> KEYWORDS = new(StringComparer.Ordinal)
    {
        "type", "enum", "const", "required", "properties", "additionalProperties", "items", "minItems", "maxItems", "uniqueItems",
        "minimum", "maximum", "minLength", "maxLength", "pattern", "allOf", "if", "then", "else"
    };

    private static readonly HashSet<string> TYPES = new(StringComparer.Ordinal) { "object", "array", "string", "number", "integer", "boolean", "null" };

    private static readonly TimeSpan PATTERN_TIMEOUT = TimeSpan.FromSeconds(2);

    #endregion

    #region Fields

    private readonly JsonElement m_root;
    private readonly Dictionary<string, Regex> m_patterns = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    private SmtLiveSchema(string name, JsonElement root)
    {
        Name = name;
        m_root = root;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Loads a schema from a file.
    /// </summary>
    /// <param name="path">The schema's file.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="InvalidDataException">The schema uses a keyword or a type this check does not know.</exception>
    /// <exception cref="JsonException">The file is not JSON.</exception>
    public static SmtLiveSchema Load(string path)
    {
        return Parse(Path.GetFileName(path), File.ReadAllText(path));
    }

    /// <summary>
    /// Reads a schema from its text.
    /// </summary>
    /// <param name="name">What the schema is called in a message.</param>
    /// <param name="json">The schema.</param>
    /// <returns>The schema.</returns>
    /// <exception cref="InvalidDataException">The schema uses a keyword or a type this check does not know.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static SmtLiveSchema Parse(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var schema = new SmtLiveSchema(name, document.RootElement.Clone());
        schema.Learn(schema.m_root, "#");
        return schema;
    }

    /// <summary>
    /// Says what is wrong with a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxErrors">The most faults said.</param>
    /// <returns>One sentence a fault, each starting with the JSON path of its place; none when the value is valid.</returns>
    public IReadOnlyList<string> Check(JsonElement value, int maxErrors)
    {
        var errors = new List<string>();
        Holds(m_root, value, "$", errors, Math.Max(1, maxErrors));
        return errors;
    }

    /// <summary>
    /// Learns a schema: every keyword must be one this check knows, every type one it knows, and
    /// every pattern is compiled once.
    /// </summary>
    private void Learn(JsonElement schema, string place)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return;

        if (schema.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{Name}: {place} is no schema");

        foreach (var keyword in schema.EnumerateObject())
        {
            if (ANNOTATIONS.Contains(keyword.Name))
                continue;

            if (!KEYWORDS.Contains(keyword.Name))
                throw new InvalidDataException($"{Name}: {place} uses '{keyword.Name}', which this tool does not check");

            var at = $"{place}/{keyword.Name}";
            switch (keyword.Name)
            {
                case "type":
                    foreach (var type in keyword.Value.ValueKind == JsonValueKind.Array ? keyword.Value.EnumerateArray().ToArray() : new[] { keyword.Value })
                    {
                        if (!TYPES.Contains(type.GetString() ?? ""))
                            throw new InvalidDataException($"{Name}: {at} names the type '{type}', which this tool does not check");
                    }

                    break;
                case "properties":
                    foreach (var property in keyword.Value.EnumerateObject())
                        Learn(property.Value, $"{at}/{property.Name}");
                    break;
                case "allOf":
                    var part = 0;
                    foreach (var item in keyword.Value.EnumerateArray())
                        Learn(item, $"{at}/{part++}");
                    break;
                case "items":
                case "additionalProperties":
                case "if":
                case "then":
                case "else":
                    Learn(keyword.Value, at);
                    break;
                case "pattern":
                    var pattern = keyword.Value.GetString() ?? "";
                    if (!m_patterns.ContainsKey(pattern))
                        m_patterns[pattern] = new Regex(Anchored(pattern), RegexOptions.CultureInvariant, PATTERN_TIMEOUT);
                    break;
            }
        }
    }

    private bool Holds(JsonElement schema, JsonElement value, string path, List<string>? errors, int max)
    {
        if (schema.ValueKind == JsonValueKind.True)
            return true;

        if (schema.ValueKind == JsonValueKind.False)
            return Fail(errors, max, path, "is not allowed here");

        // A value of the wrong type is said once: the other keywords would only repeat it.
        if (schema.TryGetProperty("type", out var type) && !IsOfType(type, value))
            return Fail(errors, max, path, $"is {KindOf(value)}, not {Shown(type)}");

        var holds = true;
        foreach (var keyword in schema.EnumerateObject())
        {
            if (errors != null && errors.Count >= max)
                return false;

            switch (keyword.Name)
            {
                case "enum":
                    if (!keyword.Value.EnumerateArray().Any(allowed => AreEqual(allowed, value)))
                        holds = Fail(errors, max, path, $"is {Shown(value)}, none of {Shown(keyword.Value)}");
                    break;
                case "const":
                    if (!AreEqual(keyword.Value, value))
                        holds = Fail(errors, max, path, $"is {Shown(value)}, not {Shown(keyword.Value)}");
                    break;
                case "allOf":
                    foreach (var part in keyword.Value.EnumerateArray())
                        holds &= Holds(part, value, path, errors, max);
                    break;
                case "if":
                    var branch = Holds(keyword.Value, value, path, null, max) ? "then" : "else";
                    if (schema.TryGetProperty(branch, out var branchSchema))
                        holds &= Holds(branchSchema, value, path, errors, max);
                    break;
                default:
                    holds &= HoldsForKind(schema, keyword, value, path, errors, max);
                    break;
            }
        }

        return holds;
    }

    private bool HoldsForKind(JsonElement schema, JsonProperty keyword, JsonElement value, string path, List<string>? errors, int max)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => HoldsForString(keyword, value.GetString() ?? "", path, errors, max),
            JsonValueKind.Number => HoldsForNumber(keyword, value, path, errors, max),
            JsonValueKind.Array => HoldsForArray(keyword, value, path, errors, max),
            JsonValueKind.Object => HoldsForObject(schema, keyword, value, path, errors, max),
            _ => true
        };
    }

    private bool HoldsForString(JsonProperty keyword, string text, string path, List<string>? errors, int max)
    {
        switch (keyword.Name)
        {
            case "minLength":
                return text.EnumerateRunes().Count() >= keyword.Value.GetInt32() || Fail(errors, max, path, $"is shorter than {keyword.Value.GetInt32()} characters");
            case "maxLength":
                return text.EnumerateRunes().Count() <= keyword.Value.GetInt32() || Fail(errors, max, path, $"is longer than {keyword.Value.GetInt32()} characters");
            case "pattern":
                var pattern = keyword.Value.GetString() ?? "";
                return m_patterns[pattern].IsMatch(text) || Fail(errors, max, path, $"is {Shorten(text)}, which does not match {pattern}");
            default:
                return true;
        }
    }

    private static bool HoldsForNumber(JsonProperty keyword, JsonElement value, string path, List<string>? errors, int max)
    {
        return keyword.Name switch
        {
            "minimum" => Compare(value, keyword.Value) >= 0 || Fail(errors, max, path, $"is {value.GetRawText()}, below {keyword.Value.GetRawText()}"),
            "maximum" => Compare(value, keyword.Value) <= 0 || Fail(errors, max, path, $"is {value.GetRawText()}, above {keyword.Value.GetRawText()}"),
            _ => true
        };
    }

    private bool HoldsForArray(JsonProperty keyword, JsonElement value, string path, List<string>? errors, int max)
    {
        var count = value.GetArrayLength();
        switch (keyword.Name)
        {
            case "minItems":
                return count >= keyword.Value.GetInt32() || Fail(errors, max, path, $"has {count} item(s), fewer than {keyword.Value.GetInt32()}");
            case "maxItems":
                return count <= keyword.Value.GetInt32() || Fail(errors, max, path, $"has {count} item(s), more than {keyword.Value.GetInt32()}");
            case "uniqueItems":
                var distinct = value.EnumerateArray().Select(item => item.GetRawText()).Distinct(StringComparer.Ordinal).Count();
                return !keyword.Value.GetBoolean() || distinct == count || Fail(errors, max, path, "holds an item twice");
            case "items":
                var holds = true;
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    holds &= Holds(keyword.Value, item, $"{path}[{index++}]", errors, max);
                return holds;
            default:
                return true;
        }
    }

    private bool HoldsForObject(JsonElement schema, JsonProperty keyword, JsonElement value, string path, List<string>? errors, int max)
    {
        var holds = true;
        switch (keyword.Name)
        {
            case "required":
                foreach (var name in keyword.Value.EnumerateArray().Select(name => name.GetString() ?? ""))
                {
                    if (!value.TryGetProperty(name, out _))
                        holds = Fail(errors, max, path, $"lacks '{name}'");
                }

                return holds;
            case "properties":
                foreach (var property in keyword.Value.EnumerateObject())
                {
                    if (value.TryGetProperty(property.Name, out var member))
                        holds &= Holds(property.Value, member, $"{path}.{property.Name}", errors, max);
                }

                return holds;
            case "additionalProperties":
                var listed = schema.TryGetProperty("properties", out var properties) ? properties : default;
                foreach (var member in value.EnumerateObject())
                {
                    if (listed.ValueKind == JsonValueKind.Object && listed.TryGetProperty(member.Name, out _))
                        continue;

                    holds &= keyword.Value.ValueKind == JsonValueKind.False
                        ? Fail(errors, max, path, $"has '{Shorten(member.Name)}', which is no field of it")
                        : Holds(keyword.Value, member.Value, $"{path}.{member.Name}", errors, max);
                }

                return holds;
            default:
                return true;
        }
    }

    private static bool Fail(List<string>? errors, int max, string path, string sentence)
    {
        if (errors != null && errors.Count < max)
            errors.Add($"{path}: {sentence}");

        return false;
    }

    private static bool IsOfType(JsonElement type, JsonElement value)
    {
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(one => IsOfType(one.GetString() ?? "", value))
            : IsOfType(type.GetString() ?? "", value);
    }

    private static bool IsOfType(string type, JsonElement value)
    {
        return type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && IsWhole(value),
            _ => false
        };
    }

    /// <summary>A number without a fractional part, however it is written: 2, 2.0, 1e2.</summary>
    private static bool IsWhole(JsonElement value)
    {
        if (decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
            return exact == decimal.Truncate(exact);

        var number = double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(number) && number == Math.Truncate(number);
    }

    private static int Compare(JsonElement left, JsonElement right)
    {
        if (decimal.TryParse(left.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(right.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return a.CompareTo(b);

        return double.Parse(left.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)
            .CompareTo(double.Parse(right.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private static bool AreEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
            return Compare(left, right) == 0;

        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind == JsonValueKind.String
            ? string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal)
            : string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A schema's <c>$</c> is the end of the text; .NET's is also the place before a last line feed.</summary>
    private static string Anchored(string pattern)
    {
        return pattern.EndsWith('$') && !pattern.EndsWith("\\$", StringComparison.Ordinal) ? pattern[..^1] + "\\z" : pattern;
    }

    private static string KindOf(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            JsonValueKind.String => "a string",
            JsonValueKind.Number => "a number",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Null => "null",
            _ => "nothing"
        };
    }

    private static string Shown(JsonElement value)
    {
        return Shorten(value.GetRawText());
    }

    private static string Shorten(string text)
    {
        return text.Length <= SHOWN_CHARACTERS ? text : text[..SHOWN_CHARACTERS] + "...";
    }

    #endregion

    #region Properties

    /// <summary>The schema's file name.</summary>
    public string Name { get; }

    #endregion
}
