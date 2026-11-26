using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OutWit.Cloud.Documents;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// The platform's document door for the types of the Smt model package, as the server runs it.
/// The vocabulary is the types a model package publishes, found by the attribute's full name as
/// the server's scan finds them (it reads the attribute and never loads its type), so a document
/// of a type no installed controller publishes is refused. A document's value is read, and a job
/// variable's value written, by System.Text.Json under the door's convention: names in camel case,
/// read without regard to case; enums by their names; nulls left out. A value that does not read
/// is the request's format error. The server's door is no package a client can reference, so its
/// rules are restated here, each of them.
/// </summary>
internal static class SmtLiveDocumentDoor
{
    #region Constants

    /// <summary>The attribute a controller publishes a document type with, matched by its full name as the server's door matches it.</summary>
    public const string CONTRACT = "OutWit.Cloud.Documents.JobDocumentContractAttribute";

    /// <summary>The door's convention for the value of a document.</summary>
    private static readonly JsonSerializerOptions DOCUMENT_JSON = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region Functions

    /// <summary>The types the model package publishes, by their ids.</summary>
    /// <returns>Each published id and its type.</returns>
    public static IReadOnlyDictionary<string, Type> Published()
    {
        var published = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(SmtOptionsData).Assembly.GetTypes())
        {
            foreach (var attribute in type.GetCustomAttributesData().Where(attribute => attribute.AttributeType.FullName == CONTRACT))
            {
                if (attribute.ConstructorArguments is [{ Value: string id }] && !string.IsNullOrWhiteSpace(id))
                    published[id] = type;
            }
        }

        return published;
    }

    /// <summary>Reads the value of a document parameter as the server's door reads it: the resolver the request parser takes.</summary>
    /// <param name="typeId">The document's type id.</param>
    /// <param name="value">The document's value.</param>
    /// <returns>The value as its type.</returns>
    /// <exception cref="JobRequestFormatException">The type is not published, or the value is not of its shape.</exception>
    public static object Resolve(string typeId, JsonElement value)
    {
        if (!Published().TryGetValue(typeId, out var type))
            throw new JobRequestFormatException($"Document type '{typeId}' is not published by any installed controller.");

        try
        {
            return value.Deserialize(type, DOCUMENT_JSON) ?? throw new JobRequestFormatException($"Document value of type '{typeId}' deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new JobRequestFormatException($"Document value does not match the published shape of '{typeId}': {exception.Message}", exception);
        }
    }

    /// <summary>Writes a job variable's value as the server's door writes a value document.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value document; null when the value's type is not published, which the door answers with its failure.</returns>
    public static string? ValueDocument(object value)
    {
        var typeId = Published().Where(published => published.Value == value.GetType()).Select(published => published.Key).SingleOrDefault();
        if (typeId == null)
            return null;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("kind", "document");
            writer.WriteString("type", typeId);
            writer.WritePropertyName("value");
            JsonSerializer.Serialize(writer, value, value.GetType(), DOCUMENT_JSON);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads the value of a value document back into its type, as a host's binding of the vocabulary reads it.</summary>
    /// <param name="document">The value document.</param>
    /// <returns>The value as the type its document names.</returns>
    /// <exception cref="JobRequestFormatException">The document names a type that is not published.</exception>
    public static object Read(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return Resolve(parsed.RootElement.GetProperty("type").GetString()!, parsed.RootElement.GetProperty("value"));
    }

    #endregion
}
