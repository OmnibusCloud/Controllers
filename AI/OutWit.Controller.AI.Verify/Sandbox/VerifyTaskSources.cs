using System.Text;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// A task's sources as files, for a runtime whose guest file system is the task itself (Lua):
/// written into a fresh folder, preopened read-only as the guest's "/", removed after the task.
/// A name must mean the same file on every node, so the rules are those of the strictest file
/// system the fleet runs on: forward slashes only, no "." or ".." segment, none of the
/// characters Windows refuses, no name Windows reserves, no segment ending in a dot or a space,
/// no segment starting with a dash (the entry is the interpreter's first argument), and no two
/// names that differ only in case.
/// </summary>
internal static class VerifyTaskSources
{
    #region Constants

    private const int MAX_NAME_LENGTH = 200;

    private const int MAX_DEPTH = 8;

    private static readonly char[] FORBIDDEN = ['\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> RESERVED = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly UTF8Encoding UTF8 = new(encoderShouldEmitUTF8Identifier: false);

    #endregion

    #region Functions

    /// <summary>
    /// What is wrong with the task's sources as files, or null when they can be written.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The problem in words, or null.</returns>
    public static string? Problem(VerifyTaskData task)
    {
        if (task.Sources.Count == 0)
            return "the task has no sources";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in task.Sources)
        {
            var problem = NameProblem(source.Name);
            if (problem != null)
                return $"source name '{source.Name}' is not allowed: {problem}";

            if (!seen.Add(source.Name))
                return $"source name '{source.Name}' is given twice (names are compared without case)";
        }

        // A name that is both a file and a folder of another ("a" and "a/b").
        foreach (var source in task.Sources)
        {
            if (seen.Any(other => other.StartsWith(source.Name + "/", StringComparison.OrdinalIgnoreCase)))
                return $"source name '{source.Name}' is a file and a folder of another source";
        }

        return null;
    }

    /// <summary>
    /// Writes the task's sources into <paramref name="folder"/> (created), as UTF-8 without a
    /// byte order mark. Call <see cref="Problem"/> first.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="folder">The folder to write into; it must not exist yet.</param>
    public static void Write(VerifyTaskData task, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var source in task.Sources)
        {
            var path = Path.Combine(folder, source.Name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source.Content ?? "", UTF8);
        }
    }

    private static string? NameProblem(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "it is empty";

        if (name.Length > MAX_NAME_LENGTH)
            return $"it is longer than {MAX_NAME_LENGTH} characters";

        if (name.IndexOfAny(FORBIDDEN) >= 0 || name.Any(char.IsControl))
            return "it has a character a file name cannot have on every node (\\ : * ? \" < > | or a control character)";

        var segments = name.Split('/');
        if (segments.Length > MAX_DEPTH)
            return $"it is deeper than {MAX_DEPTH} folders";

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return "it starts or ends with '/' or has '//'";

            if (segment is "." or "..")
                return "it has a '.' or '..' segment";

            if (segment.EndsWith('.') || segment.EndsWith(' '))
                return "a segment ends with a dot or a space";

            if (segment.StartsWith('-'))
                return "a segment starts with '-'";

            var stem = segment.Split('.')[0];
            if (RESERVED.Contains(stem))
                return $"'{stem}' is a name Windows reserves";
        }

        return null;
    }

    #endregion
}
