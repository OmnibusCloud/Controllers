namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rule a case folder on disk obeys before a file of it is read: a link
/// inside it - a directory junction, a symbolic link to a folder or to a file
/// - is refused. A case travels as the files it holds, and a link points at
/// files that are not the case's (a junction to another folder would send
/// that folder's files as the case's), so the walk records a link and never
/// enters it, and a relative path that crosses one names no case file. The
/// root itself is the case the user opened, wherever it is. An initiator
/// applies the rule to the folder the user opened, so every initiator
/// refuses alike; the relative paths the walk gives are
/// <see cref="FoamCasePathRules"/>' to judge.
/// </summary>
public static class FoamCaseFolderRules
{
    #region Constants

    /// <summary>What a link inside the case is refused with, after its path.</summary>
    public const string LINK_REASON = "a link (a directory junction or a symbolic link) inside the case; a case travels as the files it holds, never what a link points to - copy the files into the case";

    #endregion

    #region Functions

    /// <summary>
    /// Validates a case folder: every link inside it is a finding.
    /// </summary>
    /// <param name="root">The case root (the folder holding <c>system/</c>).</param>
    /// <returns>Findings, one sentence each, in path order; empty when the folder holds no link.</returns>
    public static IReadOnlyList<string> ValidateFolder(string root)
    {
        var links = new List<string>();
        EnumerateFiles(root, links);
        return links.Select(link => $"{link}: {LINK_REASON}.").ToList();
    }

    /// <summary>
    /// The files of a case folder, hidden ones included: each by its path
    /// relative to the root with forward slashes, with its size. A link is
    /// recorded and never entered, so nothing behind one is a file of the
    /// case; a cloud placeholder is not a link.
    /// </summary>
    /// <param name="root">The case root.</param>
    /// <param name="links">Receives the relative path of every link met, in path order; null discards them.</param>
    /// <returns>The files, in path order.</returns>
    public static IReadOnlyList<(string RelativePath, long Size)> EnumerateFiles(string root, ICollection<string>? links = null)
    {
        var fullRoot = Path.GetFullPath(root);
        var files = new List<(string RelativePath, long Size)>();
        var found = new List<string>();
        Walk(new DirectoryInfo(fullRoot), fullRoot, files, found);

        files.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        found.Sort(StringComparer.Ordinal);
        if (links != null)
        {
            foreach (var link in found)
                links.Add(link);
        }

        return files;
    }

    /// <summary>
    /// Whether a relative path crosses a link: whether any of its segments,
    /// from the root down, is one. A reader that opens a case file by a
    /// relative path (an <c>#include</c> target) asks this first - the file
    /// behind a link is not the case's.
    /// </summary>
    /// <param name="root">The case root.</param>
    /// <param name="relativePath">A path relative to the root, forward or backward slashes.</param>
    /// <returns>True when a segment of the path is a link.</returns>
    public static bool CrossesLink(string root, string relativePath)
    {
        var path = Path.GetFullPath(root);
        foreach (var segment in relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, segment);
            if (IsLink(path))
                return true;
        }

        return false;
    }

    #endregion

    #region Tools

    // The folder's entries as the file system lists them, except that an
    // entry whose LinkTarget names a target (a junction, a symbolic link) is
    // recorded and never entered.
    private static void Walk(DirectoryInfo folder, string root, List<(string RelativePath, long Size)> files, List<string> links)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            var relativePath = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
            if (entry.LinkTarget != null)
            {
                links.Add(relativePath);
                continue;
            }

            if (entry is DirectoryInfo subfolder)
                Walk(subfolder, root, files, links);
            else if (entry is FileInfo file)
                files.Add((relativePath, file.Length));
        }
    }

    private static bool IsLink(string path)
    {
        FileSystemInfo entry = new DirectoryInfo(path);
        if (!entry.Exists)
            entry = new FileInfo(path);

        return entry.Exists && entry.LinkTarget != null;
    }

    #endregion
}
