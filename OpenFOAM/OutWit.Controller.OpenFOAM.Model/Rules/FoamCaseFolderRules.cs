namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rule a case folder on disk obeys before a file of it is read: a link
/// inside it - a directory junction, a symbolic link to a folder or to a file
/// - is followed when its target lies inside the case root, and refused when
/// it points outside. OpenFOAM's own tutorials keep files as links inside a
/// case (a multi-region <c>system/&lt;region&gt;/fvSolution -&gt; ../fvSolution</c>,
/// <c>0/include/*</c>), and such a link is the file or folder it points to,
/// at the link's own path; a link that leaves the case points at files that
/// are not the case's (a junction to another folder would send that folder's
/// files as the case's), so the walk records it and never enters it, and a
/// relative path that crosses one names no case file. A link is resolved hop
/// by hop from the folder that holds it, against the root as opened; a
/// target that does not exist, or a chain that loops, counts as outside. An
/// initiator applies the rule to the folder the user opened, so every
/// initiator refuses alike; the relative paths the walk gives are
/// <see cref="FoamCasePathRules"/>' to judge.
/// </summary>
public static class FoamCaseFolderRules
{
    #region Constants

    /// <summary>What a link that points outside the case is refused with, after its path.</summary>
    public const string LINK_REASON = "a link (a directory junction or a symbolic link) that points outside the case; a case travels as the files it holds, never what a link points to - copy the files into the case";

    /// <summary>How many links are followed in a chain before it is given up as a loop (about what the file systems allow).</summary>
    private const int MAX_LINK_HOPS = 40;

    /// <summary>How two paths of the file system are compared: by letter case on Windows and macOS, exactly elsewhere.</summary>
    private static readonly StringComparison PATH_COMPARISON = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PATH_COMPARER = StringComparer.FromComparison(PATH_COMPARISON);

    #endregion

    #region Functions

    /// <summary>
    /// Validates a case folder: every link inside it that points outside the
    /// case is a finding.
    /// </summary>
    /// <param name="root">The case root (the folder holding <c>system/</c>).</param>
    /// <returns>Findings, one sentence each, in path order; empty when every link of the folder stays inside it.</returns>
    public static IReadOnlyList<string> ValidateFolder(string root)
    {
        var links = new List<string>();
        EnumerateFiles(root, links);
        return links.Select(link => $"{link}: {LINK_REASON}.").ToList();
    }

    /// <summary>
    /// The files of a case folder, hidden ones included: each by its path
    /// relative to the root with forward slashes, with its size. A link
    /// whose target lies inside the case is the file (its target's size) or
    /// the folder (walked under the link's path, as OpenFOAM reads it) it
    /// points to - a folder link back to a folder being walked is left
    /// alone, so a loop lists nothing twice; a link that points outside is
    /// recorded and never entered, so nothing behind one is a file of the
    /// case. A cloud placeholder is not a link.
    /// </summary>
    /// <param name="root">The case root.</param>
    /// <param name="outsideLinks">Receives the relative path of every link that points outside the case, in path order; null discards them.</param>
    /// <returns>The files, in path order.</returns>
    public static IReadOnlyList<(string RelativePath, long Size)> EnumerateFiles(string root, ICollection<string>? outsideLinks = null)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var files = new List<(string RelativePath, long Size)>();
        var outside = new List<string>();
        var walking = new HashSet<string>(PATH_COMPARER) { fullRoot };
        Walk(new DirectoryInfo(fullRoot), string.Empty, fullRoot, walking, files, outside);

        files.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        outside.Sort(StringComparer.Ordinal);
        if (outsideLinks != null)
        {
            foreach (var link in outside)
                outsideLinks.Add(link);
        }

        return files;
    }

    /// <summary>
    /// Whether a relative path crosses a link that points outside the case:
    /// whether any of its segments, from the root down, is such a link. A
    /// reader that opens a case file by a relative path (an <c>#include</c>
    /// target) asks this first - the file behind such a link is not the
    /// case's, while one behind a link that stays inside is.
    /// </summary>
    /// <param name="root">The case root.</param>
    /// <param name="relativePath">A path relative to the root, forward or backward slashes.</param>
    /// <returns>True when a segment of the path is a link pointing outside the case.</returns>
    public static bool CrossesLink(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = fullRoot;
        foreach (var segment in relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, segment);
            var entry = EntryAt(path);
            if (entry == null)
                return false;

            TargetOf(entry, out var isLink);
            if (isLink && InsideTarget(entry, fullRoot) == null)
                return true;
        }

        return false;
    }

    #endregion

    #region Tools

    // The folder's entries as the file system lists them, each under the
    // relative folder it was reached through (a folder link's files are the
    // link's, where OpenFOAM finds them); a link resolved to what it points
    // to inside the case, or recorded as outside and never entered. The set
    // holds the real paths of the folders being walked, root first: a link
    // back to one of them is a loop and is left alone.
    private static void Walk(DirectoryInfo folder, string prefix, string root, HashSet<string> walking, List<(string RelativePath, long Size)> files, List<string> outside)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            var relativePath = prefix + entry.Name;
            TargetOf(entry, out var isLink);
            var item = isLink ? InsideTarget(entry, root) : entry;
            if (item == null)
            {
                outside.Add(relativePath);
                continue;
            }

            if (item is DirectoryInfo subfolder)
            {
                var key = Path.TrimEndingDirectorySeparator(subfolder.FullName);
                if (!walking.Add(key))
                    continue;

                Walk(subfolder, relativePath + "/", root, walking, files, outside);
                walking.Remove(key);
            }
            else if (item is FileInfo file)
            {
                files.Add((relativePath, file.Length));
            }
        }
    }

    /// <summary>
    /// The file or folder a link finally points to when every hop of the
    /// chain lies inside the root: each target resolved from the folder of
    /// the link that names it, against the root as opened. Null when a hop
    /// leaves the root, names nothing that exists, cannot be read, or the
    /// chain loops.
    /// </summary>
    private static FileSystemInfo? InsideTarget(FileSystemInfo link, string root)
    {
        var visited = new HashSet<string>(PATH_COMPARER);
        var current = link;
        for (var hop = 0; hop < MAX_LINK_HOPS; hop++)
        {
            var target = TargetOf(current, out var isLink);
            if (!isLink)
                return hop == 0 ? null : current;
            if (target == null)
                return null;

            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, Path.GetDirectoryName(current.FullName) ?? root));
            if (!visited.Add(path) || !IsInside(path, root))
                return null;

            current = EntryAt(path);
            if (current == null)
                return null;
        }

        return null;
    }

    /// <summary>The link's target as written, or null; <paramref name="isLink"/> says whether the entry is a link at all (a link whose target cannot be read is one with a null target).</summary>
    private static string? TargetOf(FileSystemInfo entry, out bool isLink)
    {
        try
        {
            var target = entry.LinkTarget;
            isLink = target != null;
            return target;
        }
        catch (IOException)
        {
            isLink = true;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            isLink = true;
            return null;
        }
    }

    /// <summary>The folder or file at a path; null when there is none (a dangling link included).</summary>
    private static FileSystemInfo? EntryAt(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryInfo(path);

        return File.Exists(path) ? new FileInfo(path) : null;
    }

    private static bool IsInside(string path, string root)
    {
        return string.Equals(path, root, PATH_COMPARISON) || path.StartsWith(root + Path.DirectorySeparatorChar, PATH_COMPARISON);
    }

    #endregion
}
