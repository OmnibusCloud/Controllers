using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// Looks through a text that leaves a node, or that a lab can fetch, for anything of the machine
/// it was made on: its name, an account, a folder, the shape of a local path, and the secrets
/// the caller names (a job's tickets). What it looks for is what this machine has, so the same
/// test finds a Windows path on Windows and a home folder on Linux or macOS.
/// </summary>
internal static class VerifyLeakScan
{
    #region Constants

    private const int MIN_NEEDLE_LENGTH = 4;

    // A drive letter before a path, in a text whose backslashes were made slashes.
    private static readonly Regex DRIVE = new("(?<![a-z0-9])[a-z]:/", RegexOptions.Compiled);

    // Where an account's files and a system's scratch files live, and what the sandbox calls its own folders.
    private static readonly string[] MARKS = ["/home/", "/users/", "appdata", "/var/folders/", "/private/var/", "/tmp/", "witai_verify"];

    #endregion

    #region Functions

    /// <summary>
    /// What of this machine, or of the secrets, a text holds.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="secrets">Values that must not be in it (tickets), or null.</param>
    /// <returns>One note for each thing found; empty for a clean text.</returns>
    public static List<string> Find(string text, IEnumerable<string>? secrets = null)
    {
        var found = new List<string>();
        var normal = Normal(text);

        foreach (var (what, needle) in Needles())
        {
            if (needle.Length >= MIN_NEEDLE_LENGTH && normal.Contains(needle, StringComparison.Ordinal))
                found.Add($"{what} ({Around(normal, normal.IndexOf(needle, StringComparison.Ordinal))})");
        }

        foreach (var mark in MARKS)
        {
            if (normal.Contains(mark, StringComparison.Ordinal))
                found.Add($"'{mark}' ({Around(normal, normal.IndexOf(mark, StringComparison.Ordinal))})");
        }

        var drive = DRIVE.Match(normal);
        if (drive.Success)
            found.Add($"a drive letter ({Around(normal, drive.Index)})");

        foreach (var secret in secrets ?? [])
        {
            if (secret.Length >= MIN_NEEDLE_LENGTH && text.Contains(secret, StringComparison.OrdinalIgnoreCase))
                found.Add("a ticket");
        }

        return found;
    }

    /// <summary>Fails the test when a text holds anything of this machine or of the secrets.</summary>
    /// <param name="text">The text.</param>
    /// <param name="what">What the text is, for the message.</param>
    /// <param name="secrets">Values that must not be in it, or null.</param>
    public static void AssertClean(string text, string what, IEnumerable<string>? secrets = null)
    {
        Assert.That(Find(text, secrets), Is.Empty, what);
    }

    /// <summary>A text as the scan compares it: one kind of slash (a JSON string doubles its backslashes), one case.</summary>
    private static string Normal(string text)
    {
        return text.Replace("\\\\", "/").Replace('\\', '/').ToLowerInvariant();
    }

    private static IEnumerable<(string What, string Needle)> Needles()
    {
        yield return ("the machine's name", Normal(Environment.MachineName));
        yield return ("the account's folder", Folder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        yield return ("the account as a folder", $"/{Normal(Environment.UserName)}/");
        yield return ("the temp folder", Folder(Path.GetTempPath()));
        yield return ("the folder the tests run in", Folder(AppContext.BaseDirectory));
        yield return ("the working folder", Folder(Environment.CurrentDirectory));
    }

    private static string Folder(string path)
    {
        return Normal(path).TrimEnd('/');
    }

    private static string Around(string text, int index)
    {
        var start = Math.Max(0, index - 40);
        return text.Substring(start, Math.Min(text.Length - start, 120)).Replace('\n', ' ');
    }

    #endregion
}
