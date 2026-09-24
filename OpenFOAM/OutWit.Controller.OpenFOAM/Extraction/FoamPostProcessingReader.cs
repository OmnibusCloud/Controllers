using System.Globalization;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Reads the last row of an OpenFOAM function-object data file
/// (<c>postProcessing/&lt;name&gt;/&lt;time&gt;/*.dat</c>): comment lines start
/// with '#', the last of them names the columns, then rows of numbers; a
/// vector column is written in parentheses, which are stripped so that its
/// components become columns of their own.
/// </summary>
public static class FoamPostProcessingReader
{
    #region Functions

    /// <summary>
    /// Reads the last data row of a file.
    /// </summary>
    /// <param name="path">The data file.</param>
    /// <returns>Column names and values of the last row; null when the file has no data row.</returns>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<double> Values)? ReadLastRow(string path)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new StreamReader(path);
        return ReadLastRow(reader);
    }

    /// <summary>
    /// Reads the last data row of a text.
    /// </summary>
    /// <param name="reader">The data file's text.</param>
    /// <returns>Column names and values of the last row; null when there is no data row.</returns>
    public static (IReadOnlyList<string> Columns, IReadOnlyList<double> Values)? ReadLastRow(TextReader reader)
    {
        string? header = null;
        string? last = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith('#'))
            {
                header = trimmed;
                continue;
            }

            last = trimmed;
        }

        if (last == null)
            return null;

        var values = ValueTokens(last)
            .Select(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN)
            .ToList();

        // The header keeps its parentheses: "Cd(f)" is one column name. A
        // header that does not match the row column for column (vector
        // columns written as "(x y z)" under one name) gives way to
        // positional names.
        var names = header == null ? [] : HeaderTokens(header.TrimStart('#')).ToList();
        if (names.Count != values.Count)
            names = Enumerable.Range(0, values.Count).Select(index => index == 0 ? "Time" : $"c{index}").ToList();

        return (names, values);
    }

    private static IEnumerable<string> ValueTokens(string line)
    {
        return line
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> HeaderTokens(string line)
    {
        return line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
    }

    #endregion
}
