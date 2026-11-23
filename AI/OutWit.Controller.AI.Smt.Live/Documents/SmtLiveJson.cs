using System.Globalization;
using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// Reading the documents without trusting them: a field that is not there or not of its kind
/// reads as nothing (0, an empty text), so a document that fails its schema is still printed as
/// far as it goes. And the differences of two JSON values, by JSON path.
/// </summary>
internal static class SmtLiveJson
{
    #region Constants

    private const int SHOWN_CHARACTERS = 40;

    #endregion

    #region Functions

    /// <summary>
    /// The value at a path of field names.
    /// </summary>
    /// <param name="element">Where to start.</param>
    /// <param name="path">The field names, outermost first.</param>
    /// <returns>The value; one of kind <see cref="JsonValueKind.Undefined"/> when a field is not there.</returns>
    public static JsonElement At(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return default;
        }

        return element;
    }

    /// <summary>
    /// The whole number at a path.
    /// </summary>
    /// <param name="element">Where to start.</param>
    /// <param name="path">The field names.</param>
    /// <returns>The number; 0 when it is not there or not a whole number.</returns>
    public static long Number(JsonElement element, params string[] path)
    {
        var value = At(element, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
    }

    /// <summary>
    /// The text at a path.
    /// </summary>
    /// <param name="element">Where to start.</param>
    /// <param name="path">The field names.</param>
    /// <returns>The text; a number or a literal as JSON writes it; empty when it is not there.</returns>
    public static string Text(JsonElement element, params string[] path)
    {
        var value = At(element, path);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };
    }

    /// <summary>
    /// The counts of an object of numbers, in the object's order: <c>sat 2, unsat 1</c>.
    /// </summary>
    /// <param name="element">Where to start.</param>
    /// <param name="path">The field names of the object.</param>
    /// <returns>The counts; <c>-</c> for an empty object or none.</returns>
    public static string Counts(JsonElement element, params string[] path)
    {
        var value = At(element, path);
        if (value.ValueKind != JsonValueKind.Object)
            return "-";

        var counts = value.EnumerateObject().Select(member => $"{(member.Name.Length == 0 ? "(none)" : member.Name)} {member.Value.GetRawText()}").ToList();
        return counts.Count == 0 ? "-" : string.Join(", ", counts);
    }

    /// <summary>
    /// The counts of an object of numbers that are not 0: <c>syntax_error 2</c>.
    /// </summary>
    /// <param name="element">Where to start.</param>
    /// <param name="path">The field names of the object.</param>
    /// <returns>The counts; <c>-</c> when none is above 0.</returns>
    public static string NonZero(JsonElement element, params string[] path)
    {
        var value = At(element, path);
        if (value.ValueKind != JsonValueKind.Object)
            return "-";

        var counts = value.EnumerateObject()
            .Where(member => member.Value.ValueKind == JsonValueKind.Number && member.Value.GetRawText() != "0")
            .Select(member => $"{(member.Name.Length == 0 ? "(none)" : member.Name)} {member.Value.GetRawText()}")
            .ToList();
        return counts.Count == 0 ? "-" : string.Join(", ", counts);
    }

    /// <summary>
    /// The places two values differ, each as its JSON path and the two values, shortened.
    /// Objects are compared by their fields whatever their order, arrays item by item, numbers by
    /// their value, texts to the character.
    /// </summary>
    /// <param name="left">One value.</param>
    /// <param name="right">The other.</param>
    /// <param name="path">The path of the two values.</param>
    /// <param name="differences">Where the differences go.</param>
    /// <param name="max">The most differences kept.</param>
    public static void Differences(JsonElement left, JsonElement right, string path, List<string> differences, int max)
    {
        if (differences.Count >= max)
            return;

        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
        {
            var names = left.EnumerateObject().Select(member => member.Name)
                .Union(right.EnumerateObject().Select(member => member.Name), StringComparer.Ordinal);
            foreach (var name in names)
                Differences(At(left, name), At(right, name), $"{path}.{name}", differences, max);

            return;
        }

        if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array)
        {
            var count = Math.Max(left.GetArrayLength(), right.GetArrayLength());
            for (var index = 0; index < count; index++)
            {
                Differences(index < left.GetArrayLength() ? left[index] : default, index < right.GetArrayLength() ? right[index] : default,
                    $"{path}[{index}]", differences, max);
            }

            return;
        }

        if (!AreEqual(left, right))
            differences.Add($"{path}: {Shown(left)} vs {Shown(right)}");
    }

    /// <summary>
    /// Whether two values are the same: objects by their fields whatever their order, arrays item
    /// by item, numbers by their value, texts to the character.
    /// </summary>
    /// <param name="left">One value.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they are the same.</returns>
    public static bool AreSame(JsonElement left, JsonElement right)
    {
        var differences = new List<string>();
        Differences(left, right, "", differences, 1);
        return differences.Count == 0;
    }

    private static bool AreEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => NumbersEqual(left, right),
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }

    private static bool NumbersEqual(JsonElement left, JsonElement right)
    {
        if (decimal.TryParse(left.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(right.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return a == b;

        return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
    }

    private static string Shown(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            return "(absent)";

        var text = value.GetRawText();
        return text.Length <= SHOWN_CHARACTERS ? text : text[..SHOWN_CHARACTERS] + "...";
    }

    #endregion
}
