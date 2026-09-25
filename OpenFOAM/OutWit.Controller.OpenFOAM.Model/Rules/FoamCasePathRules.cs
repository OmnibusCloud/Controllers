namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rules the base case tree obeys: every file at a relative path inside
/// the case, with forward slashes, without whitespace (OpenFOAM strips
/// whitespace from paths), no two files at the same path - not even at two
/// paths that differ by case alone, which one file on every Windows and
/// macOS node - and nothing left over from an earlier run (a log, a
/// <c>postProcessing/</c> or <c>processor*</c> tree, which would pass for this
/// run's).
/// </summary>
public static class FoamCasePathRules
{
    #region Constants

    private const string PROCESSOR_PREFIX = "processor";

    #endregion

    #region Functions

    /// <summary>
    /// Validates the whole base tree.
    /// </summary>
    /// <param name="files">The case's file references.</param>
    /// <returns>Findings, one sentence each; empty when the tree may be materialised.</returns>
    public static IReadOnlyList<string> ValidateTree(IReadOnlyList<FoamFileRefData> files)
    {
        var findings = new List<string>();
        if (files.Count == 0)
        {
            findings.Add("The task carries no case files.");
            return findings;
        }

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var finding = Validate(file.RelativePath);
            if (finding != null)
            {
                findings.Add(finding);
                continue;
            }

            if (!seen.TryAdd(file.RelativePath, file.RelativePath))
            {
                var first = seen[file.RelativePath];
                findings.Add(first == file.RelativePath
                    ? $"{file.RelativePath}: the case carries this file twice."
                    : $"{file.RelativePath}: differs from {first} by case alone - one file on a Windows or macOS node.");
            }
        }

        return findings;
    }

    /// <summary>
    /// Checks one relative path of the base tree.
    /// </summary>
    /// <param name="relativePath">The path as the task carries it.</param>
    /// <returns>A finding, or null when the path is acceptable.</returns>
    public static string? Validate(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return "A case file has no path.";
        if (relativePath.Contains('\\'))
            return $"{relativePath}: a case file path must use forward slashes.";
        if (HasWhitespace(relativePath))
            return $"{relativePath}: a case file path must not contain a space or other whitespace (OpenFOAM strips whitespace from paths).";
        if (IsPathEscape(relativePath))
            return $"{relativePath}: a case file path must stay inside the case directory.";
        if (relativePath.Split('/').Any(segment => segment.Length == 0 || segment == "."))
            return $"{relativePath}: a case file path must not have empty or '.' segments.";

        var first = relativePath.Split('/')[0];
        if (!relativePath.Contains('/') && first.StartsWith("log.", StringComparison.Ordinal))
            return $"{relativePath}: a log of an earlier run does not belong in the base case.";
        if (first == "postProcessing")
            return $"{relativePath}: postProcessing/ of an earlier run does not belong in the base case (its values would pass for this run's).";
        if (IsProcessorDirectory(first))
            return $"{relativePath}: a decomposed case (processor*/) is not accepted; submit the reconstructed case.";

        return null;
    }

    /// <summary>
    /// Whether a path-like value would leave the case directory: absolute,
    /// drive-rooted, or with a '..' segment.
    /// </summary>
    /// <param name="value">A path or an argument value.</param>
    /// <returns>True when it escapes.</returns>
    public static bool IsPathEscape(string value)
    {
        if (value.StartsWith('/') || value.StartsWith('\\'))
            return true;
        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
            return true;

        return value.Split('/', '\\').Any(segment => segment == "..");
    }

    /// <summary>
    /// Whether a path carries whitespace of any kind - a space, a tab, a
    /// no-break space. OpenFOAM's <c>fileName</c> strips whitespace, so the
    /// path it opens is another one. The one test the case's relative paths,
    /// a run's scratch and the kit's location all go through.
    /// </summary>
    /// <param name="path">A path, relative or absolute.</param>
    /// <returns>True when any character of the path is whitespace.</returns>
    public static bool HasWhitespace(string path)
    {
        return path.Any(char.IsWhiteSpace);
    }

    private static bool IsProcessorDirectory(string segment)
    {
        return segment.StartsWith(PROCESSOR_PREFIX, StringComparison.Ordinal)
               && segment.Length > PROCESSOR_PREFIX.Length
               && segment[PROCESSOR_PREFIX.Length..].All(char.IsAsciiDigit);
    }

    #endregion
}
