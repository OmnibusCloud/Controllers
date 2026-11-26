using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryPack;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// Writes what the code gives as the application page shows it: a JSON document indented for
/// reading, an object a member a line, and a list of words or a small object of scalars on one
/// line; and a wire object of the
/// model package as JSON, its members in their wire order by their names. The page's examples
/// are held to what this writes of the code's own output.
/// </summary>
internal static class SmtExamplePrinter
{
    #region Constants

    private const string INDENT = "  ";

    /// <summary>The longest object of scalars written on one line.</summary>
    private const int INLINE_CHARACTERS = 96;

    private static readonly JsonSerializerOptions TEXT = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    #endregion

    #region Functions

    /// <summary>A JSON document indented for reading.</summary>
    /// <param name="json">The document's text.</param>
    /// <returns>The text the page shows.</returns>
    public static string Document(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        Write(builder, document.RootElement, "");
        return builder.ToString();
    }

    /// <summary>A wire object of the model package as JSON: every member it carries, in its wire order.</summary>
    /// <param name="model">The object.</param>
    /// <returns>The text the page shows.</returns>
    public static string Model(object model)
    {
        return Document(Node(model)!.ToJsonString());
    }

    #endregion

    #region Tools

    private static void Write(StringBuilder builder, JsonElement element, string indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var members = element.EnumerateObject().ToList();
                if (members.Count == 0)
                {
                    builder.Append("{}");
                    return;
                }

                // A small object of scalars reads best on one line: a count, a record of a list.
                var inline = "{" + string.Join(", ", members.Select(member => $"{JsonSerializer.Serialize(member.Name, TEXT)}: {Scalar(member.Value)}")) + "}";
                if (members.All(member => member.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) && inline.Length <= INLINE_CHARACTERS)
                {
                    builder.Append(inline);
                    return;
                }

                builder.Append("{\n");
                for (var index = 0; index < members.Count; index++)
                {
                    builder.Append(indent).Append(INDENT).Append(JsonSerializer.Serialize(members[index].Name, TEXT)).Append(": ");
                    Write(builder, members[index].Value, indent + INDENT);
                    builder.Append(index < members.Count - 1 ? ",\n" : "\n");
                }

                builder.Append(indent).Append('}');
                return;
            case JsonValueKind.Array:
                var items = element.EnumerateArray().ToList();
                if (items.All(item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
                {
                    builder.Append('[').Append(string.Join(", ", items.Select(Scalar))).Append(']');
                    return;
                }

                builder.Append("[\n");
                for (var index = 0; index < items.Count; index++)
                {
                    builder.Append(indent).Append(INDENT);
                    Write(builder, items[index], indent + INDENT);
                    builder.Append(index < items.Count - 1 ? ",\n" : "\n");
                }

                builder.Append(indent).Append(']');
                return;
            default:
                builder.Append(Scalar(element));
                return;
        }
    }

    private static string Scalar(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String ? JsonSerializer.Serialize(element.GetString(), TEXT) : element.GetRawText();
    }

    /// <summary>A value as a JSON node: a wire object by its members, a list by its items, anything else as itself.</summary>
    private static JsonNode? Node(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return JsonValue.Create(text);
            case IEnumerable items:
                return new JsonArray([.. items.Cast<object?>().Select(Node)]);
        }

        var members = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Order: property.GetCustomAttribute<MemoryPackOrderAttribute>()))
            .Where(member => member.Order != null).OrderBy(member => member.Order!.Order).ToList();
        if (members.Count == 0)
            return JsonSerializer.SerializeToNode(value);

        var node = new JsonObject();
        foreach (var (property, _) in members)
            node[property.Name] = Node(property.GetValue(value));

        return node;
    }

    #endregion
}
