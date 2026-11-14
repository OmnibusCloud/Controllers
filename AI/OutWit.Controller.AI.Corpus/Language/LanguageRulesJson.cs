using System.Text.Json;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// One language's rule data as JSON — the form a person writes and reads: the source folders
/// a language pack is built from (<c>languages/&lt;code&gt;/language.json</c>) and the frozen rule
/// data of WITLID1 models embedded in the controller. Strict: an unknown key, a wrong type or a
/// threshold the controller does not have is an error, never ignored.
/// <code>
/// {
///   "name": "Japanese",                    // optional, for people
///   "script": "Jpan",                      // ISO 15924, required
///   "stop_words": ["の", "に", "を"],      // optional (a source folder keeps them in stopwords.txt)
///   "min_stop_words": 2,                   // optional: 2 with a list, 0 without
///   "quality": {                           // optional: thresholds that differ from the script's defaults
///     "max_duplicate_line_ratio": 0.328,
///     "top_ngram_chars": [0.239, 0.196, 0.172]
///   },
///   "notes": ["where each value comes from"]   // optional, for people
/// }
/// </code>
/// </summary>
public static class LanguageRulesJson
{
    #region Constants

    private static readonly HashSet<string> KEYS = new(StringComparer.Ordinal) { "name", "script", "stop_words", "min_stop_words", "quality", "notes" };

    #endregion

    #region Functions

    /// <summary>Reads one language's rule data.</summary>
    /// <param name="code">The language code the data belongs to.</param>
    /// <param name="element">The language's JSON object.</param>
    /// <param name="extraStopWords">Stop words from elsewhere (a source folder's <c>stopwords.txt</c>); the object may not also list them.</param>
    /// <returns>The rule data.</returns>
    /// <exception cref="FormatException">The object is malformed.</exception>
    public static LanguageRules Read(string code, JsonElement element, IReadOnlyList<string>? extraStopWords = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{code}: the rule data must be a JSON object.");

        var script = "";
        var stopWords = new List<string>();
        int? minStopWords = null;
        var overrides = new List<KeyValuePair<string, IReadOnlyList<double>>>();
        var listed = false;

        foreach (var property in element.EnumerateObject())
        {
            if (!KEYS.Contains(property.Name))
                throw new FormatException($"{code}: unknown key '{property.Name}'.");

            switch (property.Name)
            {
                case "script":
                    script = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : throw new FormatException($"{code}: 'script' must be a string.");
                    break;
                case "stop_words":
                    stopWords.AddRange(Strings(code, property));
                    listed = true;
                    break;
                case "min_stop_words":
                    minStopWords = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var min) ? min : throw new FormatException($"{code}: 'min_stop_words' must be a whole number.");
                    break;
                case "quality":
                    overrides.AddRange(Quality(code, property.Value));
                    break;
                case "name":
                    if (property.Value.ValueKind != JsonValueKind.String)
                        throw new FormatException($"{code}: 'name' must be a string.");
                    break;
                case "notes":
                    if (property.Value.ValueKind != JsonValueKind.String)
                        Strings(code, property);
                    break;
            }
        }

        if (extraStopWords != null)
        {
            if (listed)
                throw new FormatException($"{code}: the stop words are listed twice (in the JSON and in stopwords.txt).");
            stopWords.AddRange(extraStopWords);
        }

        try
        {
            return new LanguageRules(code, script, stopWords, minStopWords ?? (stopWords.Count > 0 ? LanguageRules.DEFAULT_MIN_STOP_WORDS : 0), overrides);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException(exception.Message, exception);
        }
    }

    /// <summary>Reads a document of many languages: <c>{"languages": {"eng": {...}, ...}}</c> (other top-level keys are for people).</summary>
    /// <param name="json">The document.</param>
    /// <returns>The rule data, one per language.</returns>
    /// <exception cref="FormatException">The document is malformed.</exception>
    public static IReadOnlyList<LanguageRules> ReadSet(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!document.RootElement.TryGetProperty("languages", out var languages) || languages.ValueKind != JsonValueKind.Object)
            throw new FormatException("a rule-data document has a 'languages' object.");

        return languages.EnumerateObject().Select(language => Read(language.Name, language.Value)).ToList();
    }

    private static IEnumerable<KeyValuePair<string, IReadOnlyList<double>>> Quality(string code, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{code}: 'quality' must be an object.");

        foreach (var property in element.EnumerateObject())
        {
            if (QualityRuleOverrides.ValueCount(property.Name) == 0)
                throw new FormatException($"{code}: '{property.Name}' is not a quality threshold this controller has.");

            IReadOnlyList<double> values = property.Value.ValueKind switch
            {
                JsonValueKind.Number => [property.Value.GetDouble()],
                JsonValueKind.Array => property.Value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number ? item.GetDouble() : throw new FormatException($"{code}: '{property.Name}' holds numbers.")).ToList(),
                _ => throw new FormatException($"{code}: '{property.Name}' must be a number or an array of numbers.")
            };

            yield return new KeyValuePair<string, IReadOnlyList<double>>(property.Name, values);
        }
    }

    private static List<string> Strings(string code, JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{code}: '{property.Name}' must be an array of strings.");

        return property.Value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : throw new FormatException($"{code}: '{property.Name}' holds strings."))
            .ToList();
    }

    #endregion
}
