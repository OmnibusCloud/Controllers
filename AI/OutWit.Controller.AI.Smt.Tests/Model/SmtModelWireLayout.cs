using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The wire layout of the Model as it is: for every MemoryPack type its members in order, each
/// with its name, its type and the value a new instance holds. The layout is what a fleet and an
/// application are built against; <see cref="SmtModelWireLayoutTests"/> holds it to the baseline
/// committed beside it.
/// </summary>
internal static class SmtModelWireLayout
{
    #region Constants

    private const string COMMENT = "The wire layout of OutWit.Controller.AI.Smt.Model: the Model package version and, for every MemoryPack type, its members in wire order with name, type and the value a new instance holds. Once a version of the package is published, its layouts change only behind a version fence (a new controller and Model version, whose payloads no reader of the old layout is ever handed): MemoryPack throws on a payload of more members than its reader knows, so appending a member is no compatible change on its own, and a member a sender may not have arrives as its type's default (null for a text or a list), never as its initializer. A member that is added, or changes its name, its type, its place or its default, is a wire change for a deployed fleet and for every application built against the package. Update this file in the same commit as the type (SmtModelWireLayoutTests prints the current layout when it differs).";

    #endregion

    #region Functions

    /// <summary>Every MemoryPack type of the Model assembly, by name.</summary>
    /// <returns>The types in name order.</returns>
    public static List<Type> Types()
    {
        return typeof(SmtRefusalData).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute"))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The members of a type as the wire has them: in order, with name, type and default.</summary>
    /// <param name="type">The type.</param>
    /// <returns>One entry per member, by order.</returns>
    public static List<Member> Members(Type type)
    {
        var instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"{type.Name} has no parameterless constructor.");
        var nullability = new NullabilityInfoContext();

        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Order: property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order))
            .Where(member => member.Order != null)
            .OrderBy(member => member.Order)
            .Select(member => new Member(
                member.Order!.Value,
                member.Property.Name,
                Name(member.Property.PropertyType) + (IsNullableReference(member.Property, nullability) ? "?" : ""),
                Default(member.Property.GetValue(instance))))
            .ToList();
    }

    /// <summary>The whole layout as the baseline file writes it.</summary>
    /// <param name="modelVersion">The Model's version.</param>
    /// <returns>Indented JSON.</returns>
    public static string Describe(string modelVersion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("comment", COMMENT);
            writer.WriteString("modelVersion", modelVersion);
            writer.WriteStartObject("types");
            foreach (var type in Types())
            {
                writer.WriteStartArray(type.Name);
                foreach (var member in Members(type))
                    writer.WriteStringValue(member.ToString());
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    #endregion

    #region Tools

    private static string Name(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } value)
            return Name(value) + "?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    private static bool IsNullableReference(PropertyInfo property, NullabilityInfoContext nullability)
    {
        return !property.PropertyType.IsValueType && nullability.Create(property).ReadState == NullabilityState.Nullable;
    }

    private static string Default(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            Guid id => id == Guid.Empty ? "Guid.Empty" : id.ToString("D"),
            Enum name => name.ToString(),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            ICollection { Count: 0 } => "[]",
            ICollection items => $"[{items.Count} item(s)]",
            ModelBase model => $"new {model.GetType().Name}",
            _ => value.ToString() ?? ""
        };
    }

    #endregion

    #region Types

    /// <summary>One member of a type on the wire.</summary>
    /// <param name="Order">Its MemoryPack order.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Type">Its type as declared.</param>
    /// <param name="Default">The value a new instance holds.</param>
    public sealed record Member(int Order, string Name, string Type, string Default)
    {
        /// <summary>The member as the baseline writes it.</summary>
        /// <returns>For example "3: Code: String = \"\"".</returns>
        public override string ToString()
        {
            return $"{Order}: {Name}: {Type} = {Default}";
        }
    }

    #endregion
}
