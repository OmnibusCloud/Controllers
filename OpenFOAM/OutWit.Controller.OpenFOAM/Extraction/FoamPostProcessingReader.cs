using System.Globalization;
using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Reads the last row of an OpenFOAM function-object data file
/// (<c>postProcessing/&lt;name&gt;/&lt;time&gt;/*.dat</c>): comment lines start
/// with '#', the last of them names the columns, then rows of numbers; a
/// vector column is written in parentheses, which are stripped so that its
/// components become columns of their own.
/// A probes file (<c>postProcessing/&lt;name&gt;/&lt;time&gt;/&lt;field&gt;</c>) is
/// named from its own header instead: v2606 writes one <c># Probe N (x y z)</c>
/// line per probe, <c>  # Not Found</c> after one outside the mesh, then
/// <c># Time 0 1 ...</c> with the index of every probe it writes a value for,
/// and the values of a vector or tensor probe in parentheses. Each value is
/// named <c>&lt;field&gt;@probe&lt;N&gt;(x y z)</c>, with <c>_x</c>, <c>_y</c>,
/// <c>_z</c> for a vector (<c>_xx</c> ... <c>_zz</c> for a tensor, six for a
/// symmetric one) - the probe and its position as the header writes them. A
/// probe outside the mesh is written as -1e+300 (OpenFOAM's -VGREAT, measured
/// on v2606, 2026-10-28); it is left out with a note naming the probe and its
/// position, never reported as a number.
/// </summary>
public static class FoamPostProcessingReader
{
    #region Constants

    private const string TIME = "Time";

    /// <summary>What a probe column's field is separated from its number and position with.</summary>
    public const char PROBE_SEPARATOR = '@';

    /// <summary>What a probe column's number starts with after the separator: <c>probe0</c>, <c>probe1</c>, ...</summary>
    public const string PROBE_PREFIX = "probe";

    /// <summary>Beyond this magnitude a probe value is OpenFOAM's -VGREAT (1e300): a probe outside the mesh.</summary>
    private const double UNSET = 1e299;

    private const string NOT_FOUND = "Not Found";

    private static readonly Regex PROBE_LINE = new(@"^#\s*Probe\s+(\d+)\s+(\([^)]*\))(.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The component suffixes OpenFOAM orders a vector, a symmetric tensor and a tensor by.</summary>
    private static readonly IReadOnlyDictionary<int, string[]> COMPONENTS = new Dictionary<int, string[]>
    {
        [3] = ["x", "y", "z"],
        [6] = ["xx", "xy", "xz", "yy", "yz", "zz"],
        [9] = ["xx", "xy", "xz", "yx", "yy", "yz", "zx", "zy", "zz"]
    };

    private static readonly char[] SEPARATORS = [' ', '\t'];

    #endregion

    #region Functions

    /// <summary>
    /// Reads the last data row of a file; a probes file is named by its field, the file's name.
    /// </summary>
    /// <param name="path">The data file.</param>
    /// <returns>The table; null when the file does not exist or has no data row.</returns>
    public static FoamPostProcessingTable? ReadLastRow(string path)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new StreamReader(path);
        return ReadLastRow(reader, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// Reads the last data row of a text.
    /// </summary>
    /// <param name="reader">The data file's text.</param>
    /// <param name="fieldName">The field a probes file samples (the file's name); null reads a probes file by position.</param>
    /// <returns>The table; null when there is no data row.</returns>
    public static FoamPostProcessingTable? ReadLastRow(TextReader reader, string? fieldName = null)
    {
        string? header = null;
        string? last = null;
        var probes = new List<Probe>();

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith('#'))
            {
                var probe = PROBE_LINE.Match(trimmed);
                if (probe.Success)
                    probes.Add(new Probe(int.Parse(probe.Groups[1].Value, CultureInfo.InvariantCulture), probe.Groups[2].Value, probe.Groups[3].Value.Contains(NOT_FOUND, StringComparison.Ordinal)));

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

        // The header keeps its parentheses: "Cd(f)" is one column name.
        var names = header == null ? [] : HeaderTokens(header.TrimStart('#')).ToList();

        if (probes.Count > 0 && fieldName != null && names.Count > 0 && names[0] == TIME)
        {
            var table = ProbeTable(probes, fieldName, names, values);
            if (table != null)
                return table;
        }

        // A header that does not match the row column for column (vector
        // columns written as "(x y z)" under one name) gives way to
        // positional names.
        if (names.Count != values.Count)
            names = Enumerable.Range(0, values.Count).Select(index => index == 0 ? TIME : $"c{index}").ToList();

        var notes = new List<FoamNote>();
        for (var index = 1; index < values.Count; index++)
        {
            if (IsUnset(values[index]))
            {
                notes.Add(NotReported(names[index], $"{names[index]} is -1e+300, a probe outside the mesh - not reported."));
                values[index] = double.NaN;
            }
        }

        return new FoamPostProcessingTable(names, values, notes, isProbes: false);
    }

    #endregion

    #region Tools

    // The columns of a probes file from its header: one name per probe the
    // header lists and per component the row carries; null when the row does
    // not divide into the listed probes.
    private static FoamPostProcessingTable? ProbeTable(IReadOnlyList<Probe> probes, string fieldName, IReadOnlyList<string> names, List<double> values)
    {
        var listed = new List<Probe>();
        foreach (var name in names.Skip(1))
        {
            if (!int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return null;

            var probe = probes.FirstOrDefault(candidate => candidate.Index == index);
            if (probe == null)
                return null;

            listed.Add(probe);
        }

        var count = values.Count - 1;
        if (listed.Count == 0 || count % listed.Count != 0)
            return null;

        var components = count / listed.Count;
        var suffixes = components == 1
            ? [string.Empty]
            : COMPONENTS.TryGetValue(components, out var known)
                ? known.Select(suffix => "_" + suffix).ToArray()
                : Enumerable.Range(1, components).Select(component => $"_c{component}").ToArray();

        var columns = new List<string>(values.Count) { TIME };
        var notes = new List<FoamNote>();
        var position = 1;
        foreach (var probe in listed)
        {
            var outside = probe.NotFound;
            for (var component = 0; component < components; component++, position++)
            {
                columns.Add($"{fieldName}{PROBE_SEPARATOR}{PROBE_PREFIX}{probe.Index}{probe.Position}{suffixes[component]}");
                outside |= IsUnset(values[position]);
            }

            if (!outside)
                continue;

            // One sentence for the probe, on every column it leaves empty.
            var sentence = $"probe {probe.Index} {probe.Position} of {fieldName} is outside the mesh - not reported.";
            for (var component = components; component >= 1; component--)
            {
                values[position - component] = double.NaN;
                notes.Add(NotReported(columns[position - component], sentence));
            }
        }

        return new FoamPostProcessingTable(columns, values, notes, isProbes: true);
    }

    private static FoamNote NotReported(string column, string sentence)
    {
        return new FoamNote { Kind = FoamNoteKind.Warning, Column = column, Text = sentence };
    }

    private static bool IsUnset(double value)
    {
        return value <= -UNSET;
    }

    private static IEnumerable<string> ValueTokens(string line)
    {
        return line
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> HeaderTokens(string line)
    {
        return line.Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    #endregion

    #region Classes

    /// <summary>A probe as its header line describes it: its index, its position as written, and whether the header marks it not found.</summary>
    private sealed record Probe(int Index, string Position, bool NotFound);

    #endregion
}
