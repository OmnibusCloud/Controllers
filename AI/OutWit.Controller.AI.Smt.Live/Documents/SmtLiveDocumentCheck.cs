using System.Text;
using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// Holds a document file to its schema: a JSON document as one value, a JSON lines file a row at
/// a time (a row in memory, never the file). The file must be UTF-8.
/// </summary>
internal static class SmtLiveDocumentCheck
{
    #region Constants

    /// <summary>The most faults kept of one file.</summary>
    public const int MAX_ERRORS = 5;

    private static readonly UTF8Encoding STRICT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    #endregion

    #region Functions

    /// <summary>
    /// Checks a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="schema">Its schema.</param>
    /// <param name="lines">Whether the file is JSON lines.</param>
    /// <returns>What the check came to.</returns>
    public static SmtLiveDocumentVerdict CheckFile(string path, SmtLiveSchema schema, bool lines)
    {
        var bytes = new FileInfo(path).Length;
        try
        {
            return lines ? CheckLines(path, bytes, schema) : CheckDocument(path, bytes, schema);
        }
        catch (DecoderFallbackException)
        {
            return new SmtLiveDocumentVerdict(0, bytes, 1, ["the file is not valid UTF-8"]);
        }
    }

    private static SmtLiveDocumentVerdict CheckDocument(string path, long bytes, SmtLiveSchema schema)
    {
        try
        {
            using var document = JsonDocument.Parse(STRICT.GetString(File.ReadAllBytes(path)));
            var errors = schema.Check(document.RootElement, MAX_ERRORS);
            return new SmtLiveDocumentVerdict(1, bytes, errors.Count > 0 ? 1 : 0, errors);
        }
        catch (JsonException exception)
        {
            return new SmtLiveDocumentVerdict(0, bytes, 1, [$"not one JSON value: {exception.Message}"]);
        }
    }

    private static SmtLiveDocumentVerdict CheckLines(string path, long bytes, SmtLiveSchema schema)
    {
        var errors = new List<string>();
        var items = 0;
        var faulty = 0;
        var number = 0;

        using var reader = new StreamReader(path, STRICT, detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length == 0)
            {
                faulty++;
                Keep(errors, $"line {number}: blank");
                continue;
            }

            items++;
            try
            {
                using var document = JsonDocument.Parse(line);
                var found = schema.Check(document.RootElement, MAX_ERRORS);
                if (found.Count == 0)
                    continue;

                faulty++;
                foreach (var error in found)
                    Keep(errors, $"line {number}: {error}");
            }
            catch (JsonException)
            {
                faulty++;
                Keep(errors, $"line {number}: not one JSON value");
            }
        }

        return new SmtLiveDocumentVerdict(items, bytes, faulty, errors);
    }

    private static void Keep(List<string> errors, string error)
    {
        if (errors.Count < MAX_ERRORS)
            errors.Add(error);
    }

    #endregion
}
