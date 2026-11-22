using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The names a task's sources may have: the portable set. A name becomes a file on the node for
/// the runtimes whose file system is the task (Lua, C and C++), and a name must mean the same
/// file on Windows, Linux and macOS, so the rule is the same for every runtime and is checked
/// where a taskset is split, by name:
/// <list type="bullet">
/// <item>the characters of POSIX's portable file name set, <c>A-Z a-z 0-9 . _ -</c>, and
/// <c>+</c> (for <c>.c++</c>); folders are separated by <c>/</c>;</item>
/// <item>at most <see cref="MAX_NAME_BYTES"/> bytes in all (with the client's own folder before
/// it, a longer name does not fit the 260 characters a path may have on Windows) and at most
/// <see cref="MAX_DEPTH"/> segments;</item>
/// <item>no empty segment, no <c>.</c> or <c>..</c>, none that starts with <c>-</c> (the entry is
/// an interpreter's first argument) or ends with <c>.</c> (Windows drops it), and none that
/// Windows reserves for a device (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, ...);</item>
/// <item>no two names that differ only in the case of their letters (one file on Windows and
/// macOS, two on Linux), and no name that is a file and a folder of another.</item>
/// </list>
/// Non-ASCII names are refused whole: two spellings of one accented name are one file on macOS
/// and two elsewhere.
/// </summary>
public static class VerifySourceNames
{
    #region Constants

    /// <summary>The most bytes a source name may have, folders included.</summary>
    public const int MAX_NAME_BYTES = 128;

    /// <summary>The most segments a source name may have (folders and the file).</summary>
    public const int MAX_DEPTH = 8;

    /// <summary>The characters a segment may have besides ASCII letters and digits.</summary>
    public const string PUNCTUATION = "._-+";

    /// <summary>The rule as a regular expression (the schema's pattern): the characters, the segments and the leading dash.</summary>
    public const string PATTERN = @"^(?!-)[A-Za-z0-9._+-]+(/(?!-)[A-Za-z0-9._+-]+){0,7}$";

    private const char SEPARATOR = '/';

    private static readonly HashSet<string> RESERVED = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    #endregion

    #region Functions

    /// <summary>
    /// What is wrong with a task's source names, or null when every one is portable.
    /// </summary>
    /// <param name="sources">The task's sources.</param>
    /// <returns>The problem in words that name the source, or null.</returns>
    public static string? Problem(IReadOnlyList<VerifySourceFileData> sources)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var problem = Problem(source.Name);
            if (problem != null)
                return $"source name '{Shown(source.Name)}' is not allowed: {problem}";

            if (!seen.Add(source.Name))
                return $"source name '{source.Name}' is given twice (names are compared without case)";
        }

        // A name that is both a file and a folder of another ("a" and "a/b").
        foreach (var source in sources)
        {
            if (seen.Any(other => other.StartsWith(source.Name + SEPARATOR, StringComparison.OrdinalIgnoreCase)))
                return $"source name '{source.Name}' is a file and a folder of another source";
        }

        return null;
    }

    /// <summary>
    /// What is wrong with one name, or null when it is portable.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The problem in words, or null.</returns>
    public static string? Problem(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "it is empty";

        if (name.Length > MAX_NAME_BYTES)
            return $"it is longer than {MAX_NAME_BYTES} bytes";

        foreach (var character in name)
        {
            if (!IsPortable(character) && character != SEPARATOR)
                return $"it has a character outside the portable set (A-Z a-z 0-9 {string.Join(' ', PUNCTUATION.ToCharArray())}, with / between folders)";
        }

        var segments = name.Split(SEPARATOR);
        if (segments.Length > MAX_DEPTH)
            return $"it is deeper than {MAX_DEPTH} folders";

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return "it starts or ends with '/' or has '//'";

            if (segment is "." or "..")
                return "it has a '.' or '..' segment";

            if (segment.EndsWith('.'))
                return "a segment ends with a dot";

            if (segment.StartsWith('-'))
                return "a segment starts with '-'";

            var stem = segment.Split('.')[0];
            if (RESERVED.Contains(stem))
                return $"'{stem}' is a name Windows reserves";
        }

        return null;
    }

    private static bool IsPortable(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' || PUNCTUATION.Contains(character);
    }

    /// <summary>A name as a message may show it: its portable characters, anything else as '?', and no more than the limit.</summary>
    private static string Shown(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        var shown = name.Length > MAX_NAME_BYTES ? name[..MAX_NAME_BYTES] : name;
        return string.Concat(shown.Select(character => IsPortable(character) || character == SEPARATOR ? character : '?'));
    }

    #endregion
}
