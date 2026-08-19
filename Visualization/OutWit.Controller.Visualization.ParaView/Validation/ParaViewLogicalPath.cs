using System.Text.RegularExpressions;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The package's logical path rules: relative, '/'-separated, no '..', no '.' segments, no empty
/// segments, no drive letters, no UNC or absolute roots, no URI schemes, no control characters.
/// Every file reference the state makes and every attachment the package declares must be such a
/// path; the node resolves them under the task's package root and never sees a client path.
/// </summary>
public static partial class ParaViewLogicalPath
{
    #region Constants

    private const string SEPARATOR = "/";

    #endregion

    #region Fields

    private static readonly char[] INVALID_SEGMENT_CHARS = ['<', '>', ':', '"', '|', '?', '*'];

    #endregion

    #region Functions

    /// <summary>
    /// Checks a logical path against the package rules.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>Null when the path is admissible, otherwise the violation.</returns>
    public static string? Check(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "logical path is empty";

        if (path.Length > ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS)
            return $"logical path exceeds {ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS} characters";

        if (path.Any(c => char.IsControl(c)))
            return $"logical path '{Sanitize(path)}' contains control characters";

        if (path.Contains('\\'))
            return $"logical path '{path}' must use '/' separators";

        if (path.StartsWith(SEPARATOR, StringComparison.Ordinal))
            return $"logical path '{path}' is absolute";

        if (DriveLetter().IsMatch(path))
            return $"logical path '{path}' starts with a drive letter";

        if (UriScheme().IsMatch(path))
            return $"logical path '{path}' is a URI";

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return $"logical path '{path}' contains an empty segment";

            if (segment == "..")
                return $"logical path '{path}' traverses upward";

            if (segment == ".")
                return $"logical path '{path}' contains a '.' segment";

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                return $"logical path '{path}' has a segment ending in a space or dot";

            if (segment.IndexOfAny(INVALID_SEGMENT_CHARS) >= 0)
                return $"logical path '{path}' contains a character not portable across platforms";
        }

        return null;
    }

    /// <summary>
    /// Resolves a logical path under a root directory, guaranteeing the result stays inside it.
    /// </summary>
    /// <param name="rootDirectory">Absolute package root.</param>
    /// <param name="logicalPath">An admissible logical path.</param>
    /// <returns>The absolute, normalized target path.</returns>
    /// <exception cref="InvalidOperationException">The path is inadmissible or escapes the root.</exception>
    public static string ResolveUnder(string rootDirectory, string logicalPath)
    {
        var violation = Check(logicalPath);
        if (violation != null)
            throw new InvalidOperationException(violation);

        var root = Path.GetFullPath(rootDirectory);
        var target = Path.GetFullPath(Path.Combine(root, logicalPath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"logical path '{logicalPath}' escapes the package root");

        return target;
    }

    /// <summary>
    /// Whether a value has the shape of a client path (a separator of either kind, a drive letter, a
    /// URI). Diagnostics only — file properties are identified by name and files domain, never by value.
    /// </summary>
    /// <param name="value">A property element value.</param>
    /// <returns>True when the value looks like a path.</returns>
    public static bool LooksLikePath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return value.Contains('/') || value.Contains('\\') || DriveLetter().IsMatch(value) || UriScheme().IsMatch(value);
    }

    #endregion

    #region Tools

    private static string Sanitize(string value)
    {
        return new string(value.Select(c => char.IsControl(c) ? '?' : c).ToArray());
    }

    [GeneratedRegex("^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex DriveLetter();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*://", RegexOptions.CultureInvariant)]
    private static partial Regex UriScheme();

    #endregion
}
