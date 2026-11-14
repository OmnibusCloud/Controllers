using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.Tests.Docs;

/// <summary>
/// Checks a JSON document against the part of JSON Schema (2020-12) the controller's
/// published schemas use: <c>type</c>, <c>const</c>, <c>enum</c>, <c>required</c>,
/// <c>properties</c>, <c>additionalProperties</c> (false or a schema), <c>items</c>,
/// <c>oneOf</c> and local <c>$ref</c>. Enough to say that a writer's output is what its
/// schema promises, without a validator package.
/// </summary>
internal static class CorpusSchemaCheck
{
    #region Functions

    /// <summary>Validates a document.</summary>
    /// <param name="schema">The schema's root.</param>
    /// <param name="document">The document's root.</param>
    /// <returns>The problems, each with its JSON path; empty when the document holds.</returns>
    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement document)
    {
        var problems = new List<string>();
        Check(schema, schema, document, "$", problems);
        return problems;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> problems)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Check(root, Resolve(root, reference.GetString()!), value, path, problems);
            return;
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var matches = oneOf.EnumerateArray().Count(option =>
            {
                var inner = new List<string>();
                Check(root, option, value, path, inner);
                return inner.Count == 0;
            });
            if (matches != 1)
                problems.Add($"{path}: matches {matches} of the oneOf options");
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(type, value))
        {
            problems.Add($"{path}: {value.ValueKind} is not {type}");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
            problems.Add($"{path}: {value} is not the constant {constant}");

        if (schema.TryGetProperty("enum", out var allowed) && !allowed.EnumerateArray().Any(option => JsonElement.DeepEquals(option, value)))
            problems.Add($"{path}: {value} is not one of {allowed}");

        if (value.ValueKind == JsonValueKind.Object)
            CheckObject(root, schema, value, path, problems);

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                Check(root, items, item, $"{path}[{index++}]", problems);
        }
    }

    private static void CheckObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> problems)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()!))
            {
                if (!value.TryGetProperty(name, out _))
                    problems.Add($"{path}: '{name}' is required");
            }
        }

        var properties = schema.TryGetProperty("properties", out var declared) ? declared : default;
        var additional = schema.TryGetProperty("additionalProperties", out var extra) ? extra : default;

        foreach (var property in value.EnumerateObject())
        {
            var where = $"{path}.{property.Name}";
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
                Check(root, propertySchema, property.Value, where, problems);
            else if (additional.ValueKind == JsonValueKind.False)
                problems.Add($"{where}: not a property the schema allows");
            else if (additional.ValueKind == JsonValueKind.Object)
                Check(root, additional, property.Value, where, problems);
        }
    }

    private static bool TypeMatches(JsonElement type, JsonElement value)
    {
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(option => TypeMatches(option.GetString()!, value))
            : TypeMatches(type.GetString()!, value);
    }

    private static bool TypeMatches(string type, JsonElement value)
    {
        return type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            _ => false
        };
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new NotSupportedException($"only local references are checked: {reference}");

        var target = root;
        foreach (var segment in reference[2..].Split('/'))
            target = target.GetProperty(segment);

        return target;
    }

    #endregion
}
