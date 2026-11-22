using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The source data of a language pack's supported languages, one folder per language:
/// <code>
/// languages/
///   deu/
///     language.json     the script and any thresholds of its own (<see cref="LanguageRulesJson"/>)
///     stopwords.txt     optional: one stop word per line, '#' starts a comment line
///   jpn/ ...
/// </code>
/// A folder's name is the language code — the one Common Crawl's classifier labels documents
/// with (ISO 639-3) — and the pack supports exactly the languages that have a folder.
/// </summary>
public static class LanguagePackSource
{
    #region Constants

    /// <summary>The rule data file of a language folder.</summary>
    public const string LANGUAGE_FILE = "language.json";

    /// <summary>The stop-word file of a language folder.</summary>
    public const string STOP_WORDS_FILE = "stopwords.txt";

    #endregion

    #region Functions

    /// <summary>Reads every language folder under a directory.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The supported languages' rule data.</returns>
    /// <exception cref="FormatException">A folder is malformed, or there is none.</exception>
    public static LanguageRuleSet Read(string directory)
    {
        if (!Directory.Exists(directory))
            throw new FormatException($"{directory}: no such directory.");

        var languages = new List<LanguageRules>();
        foreach (var folder in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            var code = Path.GetFileName(folder);
            if (!IsCode(code))
                throw new FormatException($"{folder}: a language folder is named by its code, lower-case letters (e.g. 'deu').");

            var languageFile = Path.Combine(folder, LANGUAGE_FILE);
            if (!File.Exists(languageFile))
                throw new FormatException($"{folder}: {LANGUAGE_FILE} is missing.");

            var stopWordsFile = Path.Combine(folder, STOP_WORDS_FILE);
            var stopWords = File.Exists(stopWordsFile) ? ReadStopWords(stopWordsFile) : null;

            using var document = JsonDocument.Parse(File.ReadAllText(languageFile), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            languages.Add(LanguageRulesJson.Read(code, document.RootElement, stopWords));
        }

        if (languages.Count == 0)
            throw new FormatException($"{directory}: no language folder.");

        return new LanguageRuleSet(languages);
    }

    /// <summary>Reads a stop-word file: one word per line; empty lines and lines starting with '#' are skipped.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The words in file order.</returns>
    public static List<string> ReadStopWords(string path)
    {
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    /// <summary>Whether a name has the shape of a language code: two to eight lower-case ASCII letters.</summary>
    /// <param name="code">The name.</param>
    /// <returns>True for a code.</returns>
    public static bool IsCode(string code)
    {
        return code.Length is >= 2 and <= 8 && code.All(char.IsAsciiLetterLower);
    }

    #endregion
}
