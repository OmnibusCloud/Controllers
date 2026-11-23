using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// Says whether a JSON value is valid by a JSON Schema, for the keywords the schemas of this
/// package use and for no other: a schema that starts using a keyword this check does not know
/// fails the tests that hold it, until the keyword is taught here. It is the tests' own reading
/// of the schema, so that a row can be held to "taken by the reader exactly when the schema
/// allows it" without a second opinion from the code under test.
/// </summary>
internal static class SmtSchemaCheck
{
    #region Constants

    private static readonly HashSet<string> ANNOTATIONS = new(StringComparer.Ordinal) { "$schema", "$id", "title", "description" };

    private static readonly HashSet<string> KEYWORDS = new(StringComparer.Ordinal)
    {
        "type", "required", "additionalProperties", "properties", "enum", "const", "pattern", "maxLength", "minItems", "maxItems",
        "uniqueItems", "items", "minimum", "maximum", "allOf", "if", "then"
    };

    #endregion

    #region Functions

    /// <summary>Whether a value is valid by a schema.</summary>
    /// <param name="schema">The schema: an object of keywords, or true or false.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when every keyword of the schema holds for the value.</returns>
    /// <exception cref="NotSupportedException">The schema uses a keyword this check does not know.</exception>
    public static bool IsValid(JsonElement schema, JsonElement value)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return schema.ValueKind == JsonValueKind.True;

        foreach (var keyword in schema.EnumerateObject())
        {
            if (!ANNOTATIONS.Contains(keyword.Name) && !KEYWORDS.Contains(keyword.Name))
                throw new NotSupportedException($"the schema uses '{keyword.Name}', which this check does not know");

            // "then" is read with its "if".
            if (!ANNOTATIONS.Contains(keyword.Name) && keyword.Name != "then" && !Holds(schema, keyword, value))
                return false;
        }

        return true;
    }

    private static bool Holds(JsonElement schema, JsonProperty keyword, JsonElement value)
    {
        var rule = keyword.Value;
        switch (keyword.Name)
        {
            case "type":
                return rule.ValueKind == JsonValueKind.Array
                    ? rule.EnumerateArray().Any(type => IsOfType(type.GetString()!, value))
                    : IsOfType(rule.GetString()!, value);
            case "enum":
                return rule.EnumerateArray().Any(allowed => AreEqual(allowed, value));
            case "const":
                return AreEqual(rule, value);
            case "allOf":
                return rule.EnumerateArray().All(part => IsValid(part, value));
            case "if":
                return !IsValid(rule, value) || !schema.TryGetProperty("then", out var then) || IsValid(then, value);
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return HoldsForString(keyword, value.GetString()!);
            case JsonValueKind.Number:
                return HoldsForNumber(keyword, value);
            case JsonValueKind.Array:
                return HoldsForArray(keyword, value);
            case JsonValueKind.Object:
                return HoldsForObject(schema, keyword, value);
            default:
                return true;
        }
    }

    private static bool HoldsForString(JsonProperty keyword, string text)
    {
        return keyword.Name switch
        {
            // A schema's "$" is the end of the text; this library's is also the place before a last line feed.
            "pattern" => Regex.IsMatch(text, keyword.Value.GetString()!.Replace("$", "\\z", StringComparison.Ordinal), RegexOptions.CultureInvariant),
            "maxLength" => text.EnumerateRunes().Count() <= keyword.Value.GetInt32(),
            _ => true
        };
    }

    private static bool HoldsForNumber(JsonProperty keyword, JsonElement value)
    {
        var number = double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return keyword.Name switch
        {
            "minimum" => number >= keyword.Value.GetDouble(),
            "maximum" => number <= keyword.Value.GetDouble(),
            _ => true
        };
    }

    private static bool HoldsForArray(JsonProperty keyword, JsonElement value)
    {
        var items = value.EnumerateArray().ToList();
        return keyword.Name switch
        {
            "minItems" => items.Count >= keyword.Value.GetInt32(),
            "maxItems" => items.Count <= keyword.Value.GetInt32(),
            "items" => items.All(item => IsValid(keyword.Value, item)),
            "uniqueItems" => !keyword.Value.GetBoolean() || items.Select(item => item.GetRawText()).Distinct(StringComparer.Ordinal).Count() == items.Count,
            _ => true
        };
    }

    private static bool HoldsForObject(JsonElement schema, JsonProperty keyword, JsonElement value)
    {
        switch (keyword.Name)
        {
            case "required":
                return keyword.Value.EnumerateArray().All(name => value.TryGetProperty(name.GetString()!, out _));
            case "properties":
                return keyword.Value.EnumerateObject().All(property => !value.TryGetProperty(property.Name, out var member) || IsValid(property.Value, member));
            case "additionalProperties":
                return keyword.Value.ValueKind != JsonValueKind.False
                       || value.EnumerateObject().All(member => schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(member.Name, out _));
            default:
                return true;
        }
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
            _ => throw new NotSupportedException($"the schema uses the type '{type}', which this check does not know")
        };
    }

    /// <summary>A number without a fractional part, however it is written: 2, 2.0, 1e2.</summary>
    private static bool IsWhole(JsonElement value)
    {
        if (decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
            return exact == decimal.Truncate(exact);

        var number = double.Parse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return !double.IsFinite(number) || number == Math.Truncate(number);
    }

    private static bool AreEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind == JsonValueKind.String
            ? string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal)
            : string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }

    #endregion
}
